# Unity project · new from scratch

**Open this directory in Unity Hub**, not the repository root. Recommended Editor `6000.3.24f1` (Unity 6.3 LTS). `ProjectVersion.txt` and Package Manager dependencies are supplied.

Run `Tools > Neon Void > Generate Prototype Scene` after compile to create `Assets/_NeonVoid/Scenes/Prototype_Bootstrap.unity`. The scene and `.meta` files are generated **inside Unity**, not in this ZIP; after creation commit them. Inputs: WASD/arrows movement, mouse orientation, Space movement-direction dash. Forward auto-firing is temporary, not a finished gameplay loop.

Important: `Runtime/Prototype*` scripts are **not** the production movement/combat framework, only a testbed. They operate in `Update`, have unapproved speeds, and do not implement persistence, real 60Hz simulation, plugins or an actual hardware area. Follow `docs/10_FIRST_TASK.md` to verify and replace them.

The URP package is referenced but the URP Renderer/Graphics pipeline assets must be created/assigned in the Unity Editor and verified before art work begins. This was not run in an installed Unity Editor here; the first launch may require package compatibility adjustments.
