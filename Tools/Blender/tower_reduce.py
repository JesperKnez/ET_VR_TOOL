"""
Polygon reduction + UVs for TowerConstellation (Blender 4.x / 5.x). Run tower_cleanup.py first.

Usage: save the .blend, select the tower object, Scripting tab -> Open -> this file -> Run Script.
Report: Text Editor, text block "tower_reduce_report". DRY_RUN = True only measures.

Bellows and weld rings are lathe shapes (a profile swept around an axis). Decimate crumples them,
and removing segments from David's topology proved fragile. So this script MEASURES each one
(axis, inner/outer radius, number of ribs) and REBUILDS it as a clean, low-poly lathe shape
with the same material. Works on the original mesh as well as on an already decimated one.

Steps:
  1. Bellows      -> rebuilt with BELLOWS_SEGMENTS around, sharp V-shaped ribs
  2. Weld rings   -> rebuilt as a torus, WELD_SEGMENTS x WELD_SIDES
  3. Tiny parts   -> deleted (except bolt decals)
  4. UVs          -> bolt decals: Reset (each quad gets the whole decal); rest: Smart UV Project
The original mesh is kept as "<name>_before_reduce" (fake user).
"""

import math

import bmesh
import bpy
from mathutils import Vector

DRY_RUN = False
REBUILD_BELLOWS = False      # off: 23 ribs need ~88 profile points (only ~45% saved) and smooth tubes were misread as bellows
REBUILD_WELDS = True
DELETE_TINY = True
MAKE_UVS = True

BELLOWS_MIN_TRIS = 1000      # also catches already decimated bellows
BELLOWS_MIN_RIBS = 8
BELLOWS_MAX_DIAMETER = 1.6   # m; the tanks (2-2.5 m) are never bellows
BELLOWS_SEGMENTS = 32        # around
ROUNDNESS_TOLERANCE = 0.08   # largest radius must be within 8% of half the diameter (a square box is ~41% off)
PROFILE_BINS = 160           # slices along the axis when measuring the silhouette
PROFILE_EPSILON = 0.003      # m; profile points closer than 3 mm to a straight line are dropped
RIB_MIN_DEPTH = 0.01         # m; a rib must rise and fall at least 1 cm

WELD_MATERIAL = "WeldTube"
WELD_MAX_THICKNESS = 0.05    # m
WELD_SEGMENTS = 48           # around the tank
WELD_SIDES = 4               # around the bead itself (2 cm thick, 4 is plenty from the control room)

TINY_SIZE = 0.03             # m
TINY_KEEP_MATERIALS = {"BoltDecal"}

DECAL_MATERIAL = "BoltDecal"
UV_ANGLE_LIMIT = 66.0        # degrees, Smart UV Project
UV_ISLAND_MARGIN = 0.002

report_lines = []


def log(line=""):
    print(line)
    report_lines.append(line)


def tris(face):
    return len(face.verts) - 2


def total_tris(bm):
    return sum(tris(f) for f in bm.faces)


def slot_name(obj, index):
    if index < len(obj.material_slots) and obj.material_slots[index].material:
        return obj.material_slots[index].material.name
    return f"slot {index}"


def parts_of(bm, obj):
    """Loose parts with their faces, verts, tri count, bounding box and material."""
    bm.faces.ensure_lookup_table()
    bm.faces.index_update()
    seen = set()
    parts = []
    for start in bm.faces:
        if start.index in seen:
            continue
        seen.add(start.index)
        stack, faces = [start], []
        while stack:
            face = stack.pop()
            faces.append(face)
            for edge in face.edges:
                for other in edge.link_faces:
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
        verts = {v for f in faces for v in f.verts}
        lo = Vector([min(v.co[i] for v in verts) for i in range(3)])
        hi = Vector([max(v.co[i] for v in verts) for i in range(3)])
        parts.append({
            "faces": faces,
            "verts": verts,
            "tris": sum(tris(f) for f in faces),
            "lo": lo,
            "hi": hi,
            "size": max(hi - lo),
            "material": slot_name(obj, faces[0].material_index),
            "material_index": faces[0].material_index,
        })
    return parts


def lathe_frame(part):
    """Axis = the bounding-box dimension that differs from the other two (those two are the diameter)."""
    d = part["hi"] - part["lo"]
    axis = max(range(3), key=lambda i: min(abs(d[i] - d[j]) for j in range(3) if j != i))
    u, w = [i for i in range(3) if i != axis]
    center = (part["lo"] + part["hi"]) * 0.5
    return axis, u, w, center, d


def add_lathe(bm, center, axis, u, w, profile, segments, material_index, closed=False, sharp_rows=False):
    """Sweeps a profile [(axial position, radius), ...] around the axis. Returns the new faces."""
    rings = []
    for axial, radius in profile:
        ring = []
        for s in range(segments):
            t = 2.0 * math.pi * s / segments
            co = center.copy()
            co[axis] = axial
            co[u] += radius * math.cos(t)
            co[w] += radius * math.sin(t)
            ring.append(bm.verts.new(co))
        rings.append(ring)
    if closed:
        rings.append(rings[0])

    faces = []
    for i in range(len(rings) - 1):
        for s in range(segments):
            s2 = (s + 1) % segments
            face = bm.faces.new((rings[i][s], rings[i][s2], rings[i + 1][s2], rings[i + 1][s]))
            face.material_index = material_index
            face.smooth = True
            faces.append(face)

    if sharp_rows:
        # Crisp rib edges (peaks and valleys) while the surface around stays smooth
        for ring in rings[1:-1]:
            for s in range(segments):
                edge = bm.edges.get((ring[s], ring[(s + 1) % segments]))
                if edge:
                    edge.smooth = False

    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def measure_profile(part, axis, u, w, center, bins=PROFILE_BINS):
    """
    Silhouette of a round part: the largest radius per slice along the axis.
    Returns the profile [(axial, radius)], valley/peak radius and rib count.
    Peak and valley come from percentiles, so wider end collars do not hide the ribs.
    """
    a0, a1 = part["lo"][axis], part["hi"][axis]
    length = max(a1 - a0, 1e-6)
    best = [None] * bins
    for v in part["verts"]:
        b = min(bins - 1, int((v.co[axis] - a0) / length * bins))
        r = math.hypot(v.co[u] - center[u], v.co[w] - center[w])
        if best[b] is None or r > best[b]:
            best[b] = r

    # Empty slices (few vertices there): interpolate between the neighbours
    known = [i for i, x in enumerate(best) if x is not None]
    for i in range(bins):
        if best[i] is None:
            left = max((k for k in known if k < i), default=None)
            right = min((k for k in known if k > i), default=None)
            if left is None:
                best[i] = best[right]
            elif right is None:
                best[i] = best[left]
            else:
                t = (i - left) / (right - left)
                best[i] = best[left] * (1 - t) + best[right] * t

    values = sorted(best)
    r_valley = values[len(values) // 10]
    r_peak = values[(len(values) * 3) // 4]

    # A rib = the radius goes up at least RIB_MIN_DEPTH and comes down again at least as much.
    # Wide end flanges don't disturb this: the first one only goes down, the last one only up.
    ribs, going_up = 0, False
    low = high = best[0]
    for r in best:
        if not going_up:
            low = min(low, r)
            if r > low + RIB_MIN_DEPTH:
                going_up, high = True, r
        else:
            high = max(high, r)
            if r < high - RIB_MIN_DEPTH:
                ribs += 1
                going_up, low = False, r

    profile = [(a0 + length * (i + 0.5) / bins, r) for i, r in enumerate(best)]
    return profile, r_valley, r_peak, max(best), ribs


def simplify(points, epsilon):
    """Ramer-Douglas-Peucker: keeps the corners of the profile (rib peaks/valleys, collars), drops the rest."""
    if len(points) < 3:
        return points
    (x0, y0), (x1, y1) = points[0], points[-1]
    dx, dy = x1 - x0, y1 - y0
    norm = math.hypot(dx, dy) or 1e-9
    index, dist = 0, 0.0
    for i in range(1, len(points) - 1):
        d = abs(dy * (points[i][0] - x0) - dx * (points[i][1] - y0)) / norm
        if d > dist:
            index, dist = i, d
    if dist <= epsilon:
        return [points[0], points[-1]]
    return simplify(points[:index + 1], epsilon)[:-1] + simplify(points[index:], epsilon)


def rebuild_bellows(obj):
    me = obj.data
    bm = bmesh.from_edit_mesh(me)
    found, rejected = [], []
    for p in parts_of(bm, obj):
        if p["tris"] < BELLOWS_MIN_TRIS:
            continue
        axis, u, w, center, d = lathe_frame(p)
        diameter = max(d[u], d[w])
        if diameter > BELLOWS_MAX_DIAMETER or abs(d[u] - d[w]) > 0.05 * diameter:
            continue
        profile, r_valley, r_peak, r_max, ribs = measure_profile(p, axis, u, w, center)
        round_enough = abs(r_max - diameter * 0.5) <= ROUNDNESS_TOLERANCE * diameter * 0.5
        if round_enough and ribs >= BELLOWS_MIN_RIBS:
            # Close the ends at the valley radius, so the end faces of the collars exist too
            a0, a1 = profile[0][0] - (profile[1][0] - profile[0][0]) * 0.5, profile[-1][0] + (profile[1][0] - profile[0][0]) * 0.5
            outline = [(a0, r_valley)] + [(a0, profile[0][1])] + profile + [(a1, profile[-1][1]), (a1, r_valley)]
            outline = simplify(outline, PROFILE_EPSILON)
            # Drop points on top of each other (e.g. collar radius equal to valley radius): no zero-area faces
            outline = [pt for i, pt in enumerate(outline)
                       if i == 0 or math.dist(pt, outline[i - 1]) > 0.001]
            found.append((p, axis, u, w, center, outline, r_valley, r_peak, ribs))
        else:
            reason = "not round" if not round_enough else f"{ribs} ribs"
            rejected.append((p["tris"], reason, diameter))

    before = sum(f[0]["tris"] for f in found)
    after = sum(BELLOWS_SEGMENTS * (len(f[5]) - 1) * 2 for f in found)
    log(f"Bellows: {len(found)} found, {before:,} tris -> ~{after:,} tris")
    for p, *_, outline, r_valley, r_peak, ribs in found[:12]:
        log(f"   {ribs} ribs, radius {r_valley:.3f}-{r_peak:.3f} m, {p['tris']:,} -> "
            f"{BELLOWS_SEGMENTS * (len(outline) - 1) * 2:,} tris ({len(outline)} profile points)")
    if rejected:
        log("   Largest round-ish parts NOT recognised (tris, reason, diameter):")
        for t, reason, dia in sorted(rejected, key=lambda r: -r[0])[:8]:
            log(f"   {t:,} tris, {reason}, {dia:.2f} m")
    if DRY_RUN or not found:
        return

    for p, axis, u, w, center, outline, *_ in found:
        add_lathe(bm, center, axis, u, w, outline, BELLOWS_SEGMENTS, p["material_index"], sharp_rows=True)
        bmesh.ops.delete(bm, geom=p["faces"], context='FACES')

    bmesh.update_edit_mesh(me)
    log(f"   now {total_tris(bm):,} tris in total")


def rebuild_welds(obj):
    me = obj.data
    bm = bmesh.from_edit_mesh(me)
    found = []
    for p in parts_of(bm, obj):
        if p["material"] != WELD_MATERIAL:
            continue
        axis, u, w, center, d = lathe_frame(p)
        thickness = d[axis]
        diameter = max(d[u], d[w])
        if thickness <= WELD_MAX_THICKNESS and diameter > 10 * thickness and abs(d[u] - d[w]) < 0.05 * diameter:
            found.append((p, axis, u, w, center, thickness, diameter))

    new_tris = WELD_SEGMENTS * WELD_SIDES * 2
    before = sum(f[0]["tris"] for f in found)
    log(f"Weld rings: {len(found)} found, {before:,} tris -> {len(found) * new_tris:,} tris")
    if DRY_RUN or not found:
        return

    for p, axis, u, w, center, thickness, diameter in found:
        minor = thickness * 0.5
        major = diameter * 0.5 - minor
        profile = []
        for k in range(WELD_SIDES):
            phi = 2.0 * math.pi * k / WELD_SIDES
            profile.append((center[axis] + minor * math.sin(phi), major + minor * math.cos(phi)))
        add_lathe(bm, center, axis, u, w, profile, WELD_SEGMENTS, p["material_index"], closed=True)
        bmesh.ops.delete(bm, geom=p["faces"], context='FACES')

    bmesh.update_edit_mesh(me)
    log(f"   now {total_tris(bm):,} tris in total")


def delete_tiny(obj):
    me = obj.data
    bm = bmesh.from_edit_mesh(me)
    targets = [p for p in parts_of(bm, obj)
               if p["size"] < TINY_SIZE and p["material"] not in TINY_KEEP_MATERIALS]
    log(f"Tiny parts (< {TINY_SIZE * 100:.0f} cm): {len(targets)} parts, {sum(p['tris'] for p in targets):,} tris -> delete")
    if DRY_RUN or not targets:
        return

    bmesh.ops.delete(bm, geom=[f for p in targets for f in p["faces"]], context='FACES')
    bmesh.update_edit_mesh(me)


def select_faces(bm, predicate):
    for v in bm.verts:
        v.select = False
    for e in bm.edges:
        e.select = False
    count = 0
    for f in bm.faces:
        f.select = predicate(f)
        count += f.select
    bm.select_flush(True)
    return count


def make_uvs(obj):
    me = obj.data
    bm = bmesh.from_edit_mesh(me)
    bm.loops.layers.uv.verify()
    decal_index = next((i for i, s in enumerate(obj.material_slots)
                        if s.material and s.material.name == DECAL_MATERIAL), -1)

    decals = select_faces(bm, lambda f: f.material_index == decal_index)
    bmesh.update_edit_mesh(me)
    if decals:
        bpy.ops.uv.reset()

    bm = bmesh.from_edit_mesh(me)
    rest = select_faces(bm, lambda f: f.material_index != decal_index)
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.smart_project(angle_limit=math.radians(UV_ANGLE_LIMIT), island_margin=UV_ISLAND_MARGIN)
    log(f"UVs: {decals:,} bolt-decal faces reset, {rest:,} faces Smart UV Project")


def main():
    obj = bpy.context.active_object
    if obj is None or obj.type != 'MESH':
        raise RuntimeError("Select the tower mesh (active object) first.")
    if obj.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    if not DRY_RUN:
        backup = obj.data.copy()
        backup.name = obj.data.name + "_before_reduce"
        backup.use_fake_user = True

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.context.tool_settings.mesh_select_mode = (False, False, True)

    start = total_tris(bmesh.from_edit_mesh(obj.data))
    log(f"Tower reduce: {obj.name}, {start:,} tris{'  (DRY RUN)' if DRY_RUN else ''}\n")

    if REBUILD_BELLOWS:
        rebuild_bellows(obj)
    if REBUILD_WELDS:
        rebuild_welds(obj)
    if DELETE_TINY:
        delete_tiny(obj)
    if MAKE_UVS and not DRY_RUN:
        make_uvs(obj)

    end = total_tris(bmesh.from_edit_mesh(obj.data))
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')

    if not DRY_RUN:
        log(f"\n{start:,} -> {end:,} tris ({100 * (start - end) / max(start, 1):.0f}% less). "
            f"Original kept as '{backup.name}'.")
        log("Check the bellows and weld rings, then export the FBX and run Torens -> 3 in Unity.")

    text = bpy.data.texts.get("tower_reduce_report") or bpy.data.texts.new("tower_reduce_report")
    text.clear()
    text.write("\n".join(report_lines))


main()
