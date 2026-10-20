# Editable survival HUD

Installed in `Assets/Scenes/Test.unity`. The reusable canvas is
`Assets/Prefab/UI/SurvivalHUD/SurvivalHUDCanvas.prefab`.

In the scene expand `InventoryCanvas/SurvivalHUD_Editable`:

- `HotbarPanel/Slots_8_Horizontal`: eight equal slots in a GridLayoutGroup. Edit its cell size/spacing for alignment. Each slot has a shortcut Text, quantity Text, sprite Image, editable fallback glyph and selected border. Selection changes border/background without scaling the slot.
- `InventoryPanel`: five columns and four rows. `TitleBar_DragToMove` moves the panel with the left mouse button and keeps it inside the canvas. Its position persists across opening/closing in the current session; it is not written to a save game. Tab and the close button use the existing InventoryCanvas controller.
- `Vitals`: Health and Sanity have separate Text, track Image and filled Image objects. Runtime values come from PlayerController.
- `Turn_AP`: editable labels, pip Images and silhouette pieces. AP follows TurnBasedUnit.
- `EndTurn`: Button calls the same EndPlayerTurn method used by the existing keyboard input.
- `EmotionMonitor_Static_ArtOnly`: static sprite and labels; no emotion logic.
- `StatusIcons_Reserved`: inactive layout placeholders until actual condition bindings exist.

All text is native Unity UI Text, not baked into a screenshot. Change font, size, color and RectTransform in Inspector. Item icons use ItemData.Icon when assigned; otherwise ItemGlyphGraphic supplies a tintable geometry fallback. Change its shape code or assign a replacement sprite to ItemData.

Existing item click, double-click, scrolling, transfer, swapping and dropping logic remains in the original controllers. Pointer actions over the UI no longer reach world movement or item firing. The canvas scales from a 1920 x 1200 reference resolution.

The setup menu `Tools > Survival HUD > Apply reference layout to current scene` rebuilds this UI and saves the current scene. It also stores a scene backup in `Artifacts/SurvivalHUD` before rebuilding. Use it only when deliberately resetting the layout; normal adjustments should be made directly on the scene objects or prefab.

Validation reports and temporary Play Mode screenshots are in `Artifacts/SurvivalHUD`. Sample items/stat changes used by validation exist only during the disposable test session.
