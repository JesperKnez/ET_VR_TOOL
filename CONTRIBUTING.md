# Contributing

ET_VR_TOOL is a student project (XR group project, Applied Data Science & AI, Zuyd Hogeschool).
The repository is public so others can look at it, but **only the project team contributes**.

## Outside contributions

We do not accept pull requests, patches or feature requests from outside the team.
Pull requests from forks will be closed without review. You are welcome to fork the project
and build on it under the terms in [LICENSE](LICENSE).

Found a security problem (for example a leaked key)? Do not open a public issue: contact the
repository owner through their GitHub profile.

## Rules for the team

- **Never push to `main`.** `main` is protected: every change goes through a pull request.
  Force-pushing to or deleting `main` is blocked.
- Work on your own branch (`<name>/<topic>`, for example `david/tower`) and in your own test scene.
- **Only Jesper edits `Room_Scene`.** Need a change there? Describe it in your pull request.
- Change prefabs in Prefab Mode or with `Overrides → Apply`, not as loose scene overrides.
- Large binary files (`.fbx`, `.blend`, textures) go through **Git LFS**.
- Commit every asset together with its `.meta` file, and never edit or invent GUIDs.
- Keep everything you add inside `Assets/_Project/`. Do not change template or package folders.

## Secrets

API keys (Anthropic, DeepSeek, ElevenLabs, Azure) live in `Assets/_Project/Secrets/`, which is
git-ignored. **Never commit keys**, not in a config asset, a scene, a script or a commit message.
If a key ends up in a commit, tell Jesper and revoke the key at the provider straight away:
removing it from git later does not make it secret again.

Note that a player build of `Room_Scene` contains the keys. Do not share builds publicly.

## Pull requests

- Keep a pull request to one topic and describe what you changed and how you tested it
  (Play Mode, headset, or the playtest bot).
- No errors in the Unity Console.
- Jesper reviews and merges.
