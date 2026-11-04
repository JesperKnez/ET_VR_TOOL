"""
Tower cleanup for TowerConstellation (Blender 4.x / 5.x).

Usage:
  1. Save the .blend first.
  2. Select the tower object in Object Mode.
  3. Scripting tab -> Open -> this file -> Run Script.
  4. Read the report in the Text Editor (text block "tower_cleanup_report").

What it does:
  - Report: vertices/tris in total and per material slot, plus the heaviest groups of
    identical loose parts (e.g. "180x bolt of 320 tris = 57,600 tris"). That shows what to simplify.
  - Cleanup (unless REPORT_ONLY): merge by distance, delete loose vertices/edges,
    dissolve degenerate geometry, a very light limited dissolve, recalculate normals.
    The original mesh is kept as "<name>_backup" (fake user), so you can always go back.

It does NOT decimate or delete real parts; that is a choice you make based on the report.
"""

import math
from collections import defaultdict

import bmesh
import bpy

REPORT_ONLY = False          # True = only measure, change nothing
MERGE_DISTANCE = 0.0001      # m; ProBuilder/FBX exports often contain duplicate vertices
DISSOLVE_ANGLE = 0.5         # degrees; keep low, otherwise round tanks get non-planar n-gons
TOP_GROUPS = 15

report_lines = []


def log(line=""):
    print(line)
    report_lines.append(line)


def slot_name(obj, index):
    if index < len(obj.material_slots) and obj.material_slots[index].material:
        return obj.material_slots[index].material.name
    return f"slot {index}"


def tris(face):
    return len(face.verts) - 2


def report_stats(obj, bm, title):
    per_slot = defaultdict(int)
    total = 0
    for f in bm.faces:
        t = tris(f)
        total += t
        per_slot[f.material_index] += t

    log(f"== {title}: {len(bm.verts):,} vertices, {total:,} tris")
    for index, t in sorted(per_slot.items(), key=lambda kv: -kv[1]):
        log(f"   {slot_name(obj, index):<22} {t:>10,} tris  ({100 * t / max(total, 1):.0f}%)")
    return total


def loose_parts(bm):
    """Groups faces that are connected through edges."""
    bm.faces.index_update()
    seen = set()
    parts = []
    for start in bm.faces:
        if start.index in seen:
            continue
        seen.add(start.index)
        stack, part = [start], []
        while stack:
            face = stack.pop()
            part.append(face)
            for edge in face.edges:
                for other in edge.link_faces:
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
        parts.append(part)
    return parts


def report_heavy_parts(obj, bm):
    """Identical parts (same tri count, size and material) are counted together: bolts, flanges, bellows."""
    groups = defaultdict(lambda: [0, 0])  # key -> [count, tris per part]
    parts = loose_parts(bm)
    for part in parts:
        verts = {v for f in part for v in f.verts}
        lo = [min(v.co[i] for v in verts) for i in range(3)]
        hi = [max(v.co[i] for v in verts) for i in range(3)]
        size = tuple(sorted(round(hi[i] - lo[i], 2) for i in range(3)))
        part_tris = sum(tris(f) for f in part)
        key = (part_tris, size, slot_name(obj, part[0].material_index))
        groups[key][0] += 1
        groups[key][1] = part_tris

    log(f"\n== {len(parts):,} loose parts. Heaviest groups (count x tris = total):")
    ranked = sorted(groups.items(), key=lambda kv: -kv[1][0] * kv[1][1])
    for (part_tris, size, material), (count, _) in ranked[:TOP_GROUPS]:
        dims = " x ".join(f"{d:.2f}" for d in size)
        log(f"   {count:>4}x {part_tris:>7,} tris = {count * part_tris:>9,}   {material:<20} {dims} m")
    log("   Tip: a group with many copies and many tris per part (bolts, bellows) is the best")
    log("   candidate for a simpler model or a Decimate modifier (Collapse, ratio 0.2-0.5).")


def cleanup(bm):
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=MERGE_DISTANCE)

    loose_edges = [e for e in bm.edges if not e.link_faces]
    bmesh.ops.delete(bm, geom=loose_edges, context='EDGES')
    loose_verts = [v for v in bm.verts if not v.link_edges]
    bmesh.ops.delete(bm, geom=loose_verts, context='VERTS')

    bmesh.ops.dissolve_degenerate(bm, dist=MERGE_DISTANCE, edges=bm.edges[:])
    bmesh.ops.dissolve_limit(
        bm,
        angle_limit=math.radians(DISSOLVE_ANGLE),
        use_dissolve_boundaries=False,
        verts=bm.verts[:],
        edges=bm.edges[:],
        delimit={'MATERIAL', 'SEAM', 'SHARP'},
    )
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])


def main():
    obj = bpy.context.active_object
    if obj is None or obj.type != 'MESH':
        raise RuntimeError("Select the tower mesh (active object) first.")
    if obj.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    bm = bmesh.new()
    bm.from_mesh(obj.data)

    log(f"Tower cleanup: {obj.name}")
    before = report_stats(obj, bm, "Before")
    report_heavy_parts(obj, bm)

    if not REPORT_ONLY:
        backup = obj.data.copy()
        backup.name = obj.data.name + "_backup"
        backup.use_fake_user = True

        cleanup(bm)
        bm.to_mesh(obj.data)
        obj.data.update()

        log("")
        after = report_stats(obj, bm, "After")
        log(f"\nSaved {before - after:,} tris ({100 * (before - after) / max(before, 1):.0f}%). "
            f"Original mesh kept as '{backup.name}'.")
        log("Check the shading (Shade Auto Smooth) and the normals (Face Orientation overlay: all blue).")

    bm.free()

    text = bpy.data.texts.get("tower_cleanup_report") or bpy.data.texts.new("tower_cleanup_report")
    text.clear()
    text.write("\n".join(report_lines))


main()
