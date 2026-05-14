using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MatchRogue
{
    public sealed class MatchRogueGame : MonoBehaviour
    {
        [SerializeField] private int boardWidth = 9;
        [SerializeField] private int boardHeight = 11;
        private const int TileTypes = 6;
        private const float MatchPauseSeconds = 0.07f;
        private const float ClearAnimSeconds = 0.18f;
        private const float FallAnimSecondsPerCell = 0.055f;
        private const float MaxFallAnimSeconds = 0.26f;
        private const float CascadePauseSeconds = 0.08f;
        private const float HintDelaySeconds = 7f;
        private const float StrongHintDelaySeconds = 12f;
        private const int RoomsPerRun = 4;

        private static readonly int[] RoomMoveLimits = { 34, 32, 30, 34 };
        private static readonly int[] RoomTargetScores = { 14500, 23000, 36000, 56000 };
        private static readonly int[] RoomCrateCounts = { 16, 22, 30, 38 };
        private static readonly int[] RoomTwoLayerCrateCounts = { 2, 7, 13, 20 };
        private static readonly int[] RoomThreeLayerCrateCounts = { 0, 0, 3, 7 };
        private static readonly LevelConfig[] LevelConfigs =
        {
            new LevelConfig(
                1,
                9,
                11,
                34,
                new[]
                {
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    "..11111..",
                    ".1122211.",
                    "1.......1",
                    "11.....11"
                }),
            new LevelConfig(
                2,
                9,
                11,
                32,
                new[]
                {
                    ".........",
                    "1.......1",
                    "1.......1",
                    "22.....22",
                    ".........",
                    ".1111111.",
                    ".........",
                    "22.....22",
                    "1.......1",
                    "1.......1",
                    "........."
                }),
            new LevelConfig(
                3,
                9,
                11,
                30,
                new[]
                {
                    ".........",
                    "..11111..",
                    ".1222221.",
                    "..23332..",
                    "...222...",
                    ".........",
                    "11.....11",
                    "22.....22",
                    "11.....11",
                    ".........",
                    "........."
                }),
            new LevelConfig(
                4,
                9,
                11,
                34,
                new[]
                {
                    ".........",
                    ".........",
                    "111......",
                    "22211....",
                    "23322....",
                    "23332....",
                    "22222....",
                    ".........",
                    "....22222",
                    "....23332",
                    "....22222"
                })
        };

        private readonly Color[] tileColors =
        {
            new Color(0.95f, 0.22f, 0.26f),
            new Color(0.20f, 0.55f, 1.00f),
            new Color(0.21f, 0.78f, 0.36f),
            new Color(1.00f, 0.78f, 0.12f),
            new Color(0.67f, 0.33f, 0.95f),
            new Color(1.00f, 0.48f, 0.16f)
        };

        private Tile[,] board;
        private readonly List<RogueUpgrade> activeUpgrades = new List<RogueUpgrade>();
        private readonly System.Random rng = new System.Random();

        private Camera mainCamera;
        private Transform boardRoot;
        private Transform backgroundQuad;
        private Canvas canvas;
        private Text statusText;
        private Text upgradeText;
        private Text triggerText;
        private RectTransform statusRect;
        private Button[] upgradeButtons;
        private Button restartButton;
        private Button endlessButton;
        private Texture2D lineHorizontalIcon;
        private Texture2D lineVerticalIcon;
        private Texture2D bombIcon;
        private Texture2D rainbowIcon;
        private Texture2D propellerIcon;
        private Texture2D backgroundTexture;
        private Texture2D crateTexture;
        private Coroutine triggerTextRoutine;

        private Vector2Int? selected;
        private bool inputLocked;
        private bool upgradePanelOpen;
        private int room = 1;
        private int pendingRewardAfterRoom;
        private int score;
        private int runScore;
        private int baseTargetScore;
        private int targetScore;
        private int totalCrates;
        private int remainingCrates;
        private int movesRemaining;
        private int roomMoveLimit;
        private int comboChain;
        private int bestComboChain;
        private int rocketActivationCount;
        private int rainbowActivationCount;
        private int propellerActivationCount;
        private int bombSpawnClearProgress;
        private int rocketSpawnClearProgress;
        private int rainbowSpawnClearProgress;
        private int propellerSpawnClearProgress;
        private int pendingRainbowCopySpawns;
        private int pendingBridgeBombSpawns;
        private int pendingBridgeRocketSpawns;
        private float tileSpacing = 1f;
        private float tileScale = 0.82f;
        private Vector3 boardOrigin;
        private GameObject hintArrow;
        private Vector2Int? hintFrom;
        private Vector2Int? hintTo;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastClickTime;
        private float lastEffectiveActionTime;
        private int Width => boardWidth;
        private int Height => boardHeight;

        private void Awake()
        {
            var firstLevelConfig = GetLevelConfig(1);
            if (firstLevelConfig != null)
            {
                boardWidth = firstLevelConfig.BoardWidth;
                boardHeight = firstLevelConfig.BoardHeight;
            }

            boardWidth = Mathf.Clamp(boardWidth, 6, 12);
            boardHeight = Mathf.Clamp(boardHeight, 6, 14);
            board = new Tile[Width, Height];
            BuildScene();
            StartRun();
        }

        private void Update()
        {
            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            {
                ConfigureCameraAndBoardLayout();
                RefreshBoardTransforms();
            }

            if (inputLocked)
            {
                CancelHint();
                lastEffectiveActionTime = Time.unscaledTime;
                return;
            }

            if (TryGetPrimaryPressPosition(out var screenPosition))
            {
                MarkEffectiveAction();
                TrySelectTile(screenPosition);
            }

            UpdateIdleHint();
            RefreshStatus();
        }

        private void BuildScene()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                mainCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            mainCamera.orthographic = true;
            mainCamera.transform.position = new Vector3(0f, 0f, -10f);
            mainCamera.backgroundColor = new Color(0.10f, 0.09f, 0.13f);

            LoadSpecialIcons();
            LoadBackgroundTexture();
            boardRoot = new GameObject("Board").transform;
            ConfigureCameraAndBoardLayout();
            BuildBackground();
            BuildUi();
        }

        private void LoadSpecialIcons()
        {
            lineHorizontalIcon = Resources.Load<Texture2D>("SpecialIcons/LineHorizontal");
            lineVerticalIcon = Resources.Load<Texture2D>("SpecialIcons/LineVertical");
            bombIcon = Resources.Load<Texture2D>("SpecialIcons/Bomb");
            rainbowIcon = Resources.Load<Texture2D>("SpecialIcons/Rainbow");
            propellerIcon = Resources.Load<Texture2D>("SpecialIcons/Propeller");
        }

        private void LoadBackgroundTexture()
        {
            backgroundTexture = Resources.Load<Texture2D>("Backgrounds/SciFiSpace");
            crateTexture = Resources.Load<Texture2D>("Targets/Crate");
        }

        private void ConfigureCameraAndBoardLayout()
        {
            lastScreenWidth = Mathf.Max(1, Screen.width);
            lastScreenHeight = Mathf.Max(1, Screen.height);

            var aspect = lastScreenWidth / (float)lastScreenHeight;
            tileSpacing = 0.92f;
            tileScale = Mathf.Clamp(tileSpacing * 0.82f, 0.62f, 0.82f);

            var boardWidth = (Width - 1) * tileSpacing + tileScale;
            var boardHeight = (Height - 1) * tileSpacing + tileScale;
            var requiredHalfHeightForWidth = boardWidth / (2f * Mathf.Max(0.1f, aspect)) + 0.25f;
            var requiredHalfHeightForHeight = boardHeight * 0.5f + 1.75f;
            mainCamera.orthographicSize = Mathf.Max(6.2f, requiredHalfHeightForWidth, requiredHalfHeightForHeight);

            var boardCenter = new Vector3(0f, -0.75f, 0f);
            boardOrigin = boardCenter - new Vector3((Width - 1) * tileSpacing * 0.5f, (Height - 1) * tileSpacing * 0.5f, 0f);
            RefreshBackgroundTransform();
        }

        private void BuildBackground()
        {
            if (backgroundTexture != null)
            {
                var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
                background.name = "Sci-Fi Space Background";
                backgroundQuad = background.transform;
                var collider = background.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                var renderer = background.GetComponent<MeshRenderer>();
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.material.mainTexture = backgroundTexture;
                renderer.material.color = Color.white;
                RefreshBackgroundTransform();
            }

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var cell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    cell.name = $"Cell {x},{y}";
                    cell.transform.SetParent(boardRoot);
                    cell.transform.position = GridToWorld(x, y) + new Vector3(0f, 0f, 0.2f);
                    cell.transform.localScale = Vector3.one * (tileScale + 0.08f);
                    var renderer = cell.GetComponent<MeshRenderer>();
                    renderer.material = new Material(Shader.Find("Sprites/Default"));
                    renderer.material.color = (x + y) % 2 == 0
                        ? new Color(0.18f, 0.17f, 0.23f)
                        : new Color(0.14f, 0.13f, 0.18f);
                }
            }
        }

        private void RefreshBackgroundTransform()
        {
            if (backgroundQuad == null || mainCamera == null)
            {
                return;
            }

            var height = mainCamera.orthographicSize * 2f;
            var width = height * Mathf.Max(0.1f, lastScreenWidth / (float)Mathf.Max(1, lastScreenHeight));
            backgroundQuad.position = new Vector3(0f, 0f, 1.5f);
            backgroundQuad.localScale = new Vector3(width, height, 1f);
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080f, 1920f);
            canvasObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            statusText = CreateText("Status", new Vector2(32f, -132f), new Vector2(840f, 320f), 34, TextAnchor.UpperLeft);
            statusText.fontStyle = FontStyle.Bold;
            AddTextOutline(statusText, Color.black, new Vector2(3f, -3f));
            statusRect = statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            upgradeText = CreateText("UpgradeTitle", new Vector2(0f, -430f), new Vector2(1000f, 120f), 40, TextAnchor.MiddleCenter);
            upgradeText.text = "";
            triggerText = CreateText("TriggerText", new Vector2(0f, -315f), new Vector2(760f, 80f), 34, TextAnchor.MiddleCenter);
            triggerText.text = "";
            triggerText.gameObject.SetActive(false);

            upgradeButtons = new Button[3];
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                upgradeButtons[i] = CreateButton($"Upgrade {i + 1}", new Vector2(0f, 120f - i * 140f), new Vector2(860f, 108f));
            }

            restartButton = CreateButton("Restart", new Vector2(0f, -790f), new Vector2(420f, 90f));
            restartButton.GetComponentInChildren<Text>().text = "重新开始";
            restartButton.onClick.AddListener(StartRun);

            endlessButton = CreateButton("Endless", new Vector2(230f, -790f), new Vector2(360f, 90f));
            endlessButton.GetComponentInChildren<Text>().text = "开始无尽";
            endlessButton.gameObject.SetActive(false);

            SetUpgradePanel(false);
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        private Text CreateText(string name, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = GetRuntimeFont();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.96f, 0.94f, 0.88f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private void AddTextOutline(Text text, Color color, Vector2 distance)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private Button CreateButton(string name, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.22f, 0.20f, 0.29f, 0.95f);

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.34f, 0.30f, 0.45f);
            colors.pressedColor = new Color(0.14f, 0.12f, 0.20f);
            button.colors = colors;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(go.transform);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(24f, 8f);
            labelRect.offsetMax = new Vector2(-24f, -8f);

            var label = labelObject.AddComponent<Text>();
            label.font = GetRuntimeFont();
            label.fontSize = 30;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.98f, 0.96f, 0.90f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return button;
        }

        private void StartRun()
        {
            room = 1;
            pendingRewardAfterRoom = 0;
            activeUpgrades.Clear();
            selected = null;
            inputLocked = false;
            score = 0;
            runScore = 0;
            bestComboChain = 0;
            rocketActivationCount = 0;
            rainbowActivationCount = 0;
            propellerActivationCount = 0;
            bombSpawnClearProgress = 0;
            rocketSpawnClearProgress = 0;
            rainbowSpawnClearProgress = 0;
            propellerSpawnClearProgress = 0;
            pendingRainbowCopySpawns = 0;
            pendingBridgeBombSpawns = 0;
            pendingBridgeRocketSpawns = 0;
            MarkEffectiveAction();
            restartButton.GetComponentInChildren<Text>().text = "重新开始";
            restartButton.gameObject.SetActive(true);
            endlessButton.gameObject.SetActive(false);
            StartRoom();
        }

        private void StartRoom()
        {
            inputLocked = false;
            selected = null;
            GenerateBoard();
            PlaceRoomCrates();
            SeedShowcaseSpecialsForRoom();
            EnsurePlayableBoard();
            RefreshBoardTransforms();
            score = 0;
            comboChain = 0;
            var roomIndex = Mathf.Clamp(room - 1, 0, RoomsPerRun - 1);
            var levelConfig = GetLevelConfig(room);
            roomMoveLimit = levelConfig?.MoveLimit ?? RoomMoveLimits[roomIndex];
            movesRemaining = roomMoveLimit;
            baseTargetScore = RoomTargetScores[roomIndex];
            targetScore = GetAdjustedTargetScore();
            SetUpgradePanel(false);
            MarkEffectiveAction();
            RefreshStatus();
        }

        private void GenerateBoard()
        {
            ClearTiles();
            totalCrates = 0;
            remainingCrates = 0;

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var type = RollTileTypeAvoidingMatch(x, y);
                    board[x, y] = CreateTile(x, y, type);
                }
            }
        }

        private void PlaceRoomCrates()
        {
            var roomIndex = Mathf.Clamp(room - 1, 0, RoomsPerRun - 1);
            var levelConfig = GetLevelConfig(room);
            if (levelConfig != null && TryPlaceConfiguredCrates(levelConfig))
            {
                return;
            }

            var template = PickCrateTemplate(room);
            var positions = BuildCrateTemplate(template);
            ApplyCrateTemplateJitter(positions, roomIndex);
            AddFallbackCratePositions(positions);

            totalCrates = Mathf.Min(RoomCrateCounts[roomIndex], positions.Count);
            remainingCrates = totalCrates;
            var threeLayerCount = Mathf.Min(RoomThreeLayerCrateCounts[roomIndex], totalCrates);
            var layeredCandidates = positions
                .Take(totalCrates)
                .OrderBy(_ => rng.Next())
                .ToList();
            var threeLayerPositions = new HashSet<Vector2Int>(layeredCandidates.Take(threeLayerCount));
            var twoLayerCount = Mathf.Min(RoomTwoLayerCrateCounts[roomIndex], totalCrates - threeLayerCount);
            var twoLayerPositions = new HashSet<Vector2Int>(layeredCandidates
                .Skip(threeLayerCount)
                .Take(twoLayerCount));

            for (var i = 0; i < totalCrates; i++)
            {
                var pos = positions[i];
                board[pos.x, pos.y].CrateHealth = threeLayerPositions.Contains(pos) ? 3 : twoLayerPositions.Contains(pos) ? 2 : 1;
                DecorateTile(board[pos.x, pos.y]);
            }
        }

        private LevelConfig GetLevelConfig(int levelIndex)
        {
            return LevelConfigs.FirstOrDefault(level => level.LevelIndex == levelIndex);
        }

        private bool TryPlaceConfiguredCrates(LevelConfig config)
        {
            if (config.BoardWidth != Width || config.BoardHeight != Height)
            {
                Debug.LogWarning($"Level {config.LevelIndex} board size is {config.BoardWidth}x{config.BoardHeight}, current board is {Width}x{Height}. Out-of-range boxes will be skipped.");
            }

            totalCrates = 0;
            remainingCrates = 0;

            for (var row = 0; row < config.BoxMapRows.Length; row++)
            {
                var mapRow = config.BoxMapRows[row] ?? string.Empty;
                if (mapRow.Length != config.BoardWidth)
                {
                    Debug.LogWarning($"Level {config.LevelIndex} row {row} has width {mapRow.Length}, expected {config.BoardWidth}.");
                }

                // Box maps are authored from top to bottom. Runtime grid coordinates use bottom-left origin:
                // x increases left to right, y increases bottom to top.
                var y = Height - 1 - row;
                for (var x = 0; x < mapRow.Length; x++)
                {
                    var hp = mapRow[x] - '0';
                    if (mapRow[x] == '.')
                    {
                        continue;
                    }

                    if (hp < 1 || hp > 3)
                    {
                        Debug.LogWarning($"Level {config.LevelIndex} has invalid box token '{mapRow[x]}' at map row {row}, x {x}. Use '.', '1', '2', or '3'.");
                        continue;
                    }

                    var pos = new Vector2Int(x, y);
                    if (!IsInside(pos))
                    {
                        Debug.LogWarning($"Level {config.LevelIndex} box ({x},{y}) is outside the {Width}x{Height} board and was skipped.");
                        continue;
                    }

                    board[pos.x, pos.y].CrateHealth = hp;
                    DecorateTile(board[pos.x, pos.y]);
                    totalCrates++;
                }
            }

            remainingCrates = totalCrates;
            if (totalCrates <= 0)
            {
                Debug.LogWarning($"Level {config.LevelIndex} has no valid boxes. Falling back to template crate placement.");
                return false;
            }

            return true;
        }

        private CrateLayoutTemplate PickCrateTemplate(int roomNumber)
        {
            CrateLayoutTemplate[] candidates;
            switch (roomNumber)
            {
                case 1:
                    candidates = new[] { CrateLayoutTemplate.OpenTopBottomTargets, CrateLayoutTemplate.OpenCenterEdgeTargets };
                    break;
                case 2:
                    candidates = new[] { CrateLayoutTemplate.SideTargetsMiddleLane, CrateLayoutTemplate.Channel };
                    break;
                case 3:
                    candidates = new[] { CrateLayoutTemplate.OpenCenterEdgeTargets, CrateLayoutTemplate.SideTargetsMiddleLane, CrateLayoutTemplate.DenseCluster };
                    break;
                default:
                    candidates = new[] { CrateLayoutTemplate.DenseCluster, CrateLayoutTemplate.Channel, CrateLayoutTemplate.Mixed };
                    break;
            }

            return candidates[rng.Next(candidates.Length)];
        }

        private List<Vector2Int> BuildCrateTemplate(CrateLayoutTemplate template)
        {
            var positions = new List<Vector2Int>();
            switch (template)
            {
                case CrateLayoutTemplate.OpenTopBottomTargets:
                    for (var x = 0; x < Width; x++)
                    {
                        positions.Add(new Vector2Int(x, 0));
                        if (x % 2 == 0)
                        {
                            positions.Add(new Vector2Int(x, 1));
                        }
                    }

                    AddRectPositions(positions, 1, 2, Mathf.Max(1, Width - 2), 1);
                    for (var x = 1; x < Width - 1; x += 2)
                    {
                        positions.Add(new Vector2Int(x, 3));
                    }
                    break;
                case CrateLayoutTemplate.OpenCenterEdgeTargets:
                    for (var x = 0; x < Width; x++)
                    {
                        positions.Add(new Vector2Int(x, 0));
                        if (x % 2 == 0)
                        {
                            positions.Add(new Vector2Int(x, Height - 1));
                        }
                    }

                    for (var y = 1; y < Height - 1; y++)
                    {
                        if (y >= Height / 2 - 2 && y <= Height / 2 + 2)
                        {
                            continue;
                        }

                        positions.Add(new Vector2Int(0, y));
                        positions.Add(new Vector2Int(Width - 1, y));
                    }
                    break;
                case CrateLayoutTemplate.SideTargetsMiddleLane:
                    AddRectPositions(positions, 0, 1, 2, Height - 2);
                    AddRectPositions(positions, Width - 2, 1, 2, Height - 2);
                    for (var y = 2; y < Height - 2; y += 3)
                    {
                        positions.Add(new Vector2Int(2, y));
                        positions.Add(new Vector2Int(Width - 3, y));
                    }
                    break;
                case CrateLayoutTemplate.Channel:
                    if (rng.Next(2) == 0)
                    {
                        var lower = Mathf.Max(1, Height / 2 - 1);
                        for (var x = 0; x < Width; x++)
                        {
                            if (x == Width / 2)
                            {
                                continue;
                            }

                            positions.Add(new Vector2Int(x, lower));
                            positions.Add(new Vector2Int(x, lower + 1));
                        }
                    }
                    else
                    {
                        var left = Mathf.Max(1, Width / 2 - 1);
                        for (var y = 0; y < Height; y++)
                        {
                            if (y == Height / 2 || y == Height / 2 + 1)
                            {
                                continue;
                            }

                            positions.Add(new Vector2Int(left, y));
                            positions.Add(new Vector2Int(left + 1, y));
                        }
                    }

                    AddPositions(positions, new Vector2Int(1, 1), new Vector2Int(Width - 2, Height - 2), new Vector2Int(1, Height - 2), new Vector2Int(Width - 2, 1));
                    break;
                case CrateLayoutTemplate.DenseCluster:
                    AddRectPositions(positions, 1, 1, Mathf.Max(3, Width / 2), Mathf.Max(4, Height / 2));
                    AddPositions(positions,
                        new Vector2Int(Width - 2, 1), new Vector2Int(Width - 2, 2), new Vector2Int(Width - 3, 1),
                        new Vector2Int(1, Height - 2), new Vector2Int(2, Height - 2));
                    break;
                case CrateLayoutTemplate.Mixed:
                    positions.AddRange(BuildCrateTemplate(CrateLayoutTemplate.Channel).Take(20));
                    positions.AddRange(BuildCrateTemplate(CrateLayoutTemplate.DenseCluster).Take(18));
                    break;
            }

            return positions
                .Where(IsInside)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .ToList();
        }

        private void AddRectPositions(List<Vector2Int> positions, int startX, int startY, int width, int height)
        {
            for (var x = startX; x < startX + width; x++)
            {
                for (var y = startY; y < startY + height; y++)
                {
                    positions.Add(new Vector2Int(x, y));
                }
            }
        }

        private void AddPositions(List<Vector2Int> positions, params Vector2Int[] newPositions)
        {
            positions.AddRange(newPositions);
        }

        private void ApplyCrateTemplateJitter(List<Vector2Int> positions, int roomIndex)
        {
            var jitterCount = Mathf.Clamp(1 + roomIndex, 1, 4);
            for (var i = 0; i < jitterCount && positions.Count > 0; i++)
            {
                var removeIndex = rng.Next(positions.Count);
                var source = positions[removeIndex];
                var replacement = FindNearbyOpenTemplatePosition(source, positions);
                if (replacement.HasValue)
                {
                    positions[removeIndex] = replacement.Value;
                }
            }

            var shuffled = positions
                .Distinct()
                .OrderBy(_ => rng.Next())
                .ToList();
            positions.Clear();
            positions.AddRange(shuffled);
        }

        private Vector2Int? FindNearbyOpenTemplatePosition(Vector2Int source, List<Vector2Int> occupied)
        {
            var offsets = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1),
                new Vector2Int(1, 1),
                new Vector2Int(-1, -1)
            }.OrderBy(_ => rng.Next());

            foreach (var offset in offsets)
            {
                var candidate = source + offset;
                if (IsInside(candidate) && !occupied.Contains(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void AddFallbackCratePositions(List<Vector2Int> positions)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    if (!positions.Contains(pos))
                    {
                        positions.Add(pos);
                    }
                }
            }
        }

        private int RollTileTypeAvoidingMatch(int x, int y)
        {
            foreach (var type in Enumerable.Range(0, TileTypes).OrderBy(_ => rng.Next()))
            {
                if (!WouldCreateImmediateMatch(x, y, type))
                {
                    return type;
                }
            }

            return rng.Next(TileTypes);
        }

        private bool WouldCreateImmediateMatch(int x, int y, int type)
        {
            return WouldCreateLineMatch(x, y, type) || WouldCreateSquareMatch(x, y, type);
        }

        private bool WouldCreateLineMatch(int x, int y, int type)
        {
            var horizontalCount = 1 + CountMatchingPreviewTiles(x, y, -1, 0, type) + CountMatchingPreviewTiles(x, y, 1, 0, type);
            if (horizontalCount >= 3)
            {
                return true;
            }

            var verticalCount = 1 + CountMatchingPreviewTiles(x, y, 0, -1, type) + CountMatchingPreviewTiles(x, y, 0, 1, type);
            return verticalCount >= 3;
        }

        private int CountMatchingPreviewTiles(int x, int y, int dx, int dy, int type)
        {
            var count = 0;
            var current = new Vector2Int(x + dx, y + dy);
            while (IsInside(current) && IsPreviewMatchTile(current, type))
            {
                count++;
                current += new Vector2Int(dx, dy);
            }

            return count;
        }

        private bool WouldCreateSquareMatch(int x, int y, int type)
        {
            for (var offsetX = -1; offsetX <= 0; offsetX++)
            {
                for (var offsetY = -1; offsetY <= 0; offsetY++)
                {
                    var originX = x + offsetX;
                    var originY = y + offsetY;
                    if (originX < 0 || originY < 0 || originX + 1 >= Width || originY + 1 >= Height)
                    {
                        continue;
                    }

                    var formsSquare = true;
                    for (var squareX = originX; squareX <= originX + 1; squareX++)
                    {
                        for (var squareY = originY; squareY <= originY + 1; squareY++)
                        {
                            if (squareX == x && squareY == y)
                            {
                                continue;
                            }

                            if (!IsPreviewMatchTile(new Vector2Int(squareX, squareY), type))
                            {
                                formsSquare = false;
                                break;
                            }
                        }

                        if (!formsSquare)
                        {
                            break;
                        }
                    }

                    if (formsSquare)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsPreviewMatchTile(Vector2Int pos, int type)
        {
            var tile = board[pos.x, pos.y];
            return tile != null && tile.Special == SpecialKind.None && tile.CrateHealth <= 0 && tile.Type == type;
        }

        private Tile CreateTile(int x, int y, int type)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"Tile {x},{y}";
            go.transform.SetParent(boardRoot);
            go.transform.position = GridToWorld(x, y);
            go.transform.localScale = Vector3.one * tileScale;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = tileColors[type];

            return new Tile(type, SpecialKind.None, go);
        }

        private void DecorateTile(Tile tile)
        {
            if (tile.Object == null)
            {
                return;
            }

            for (var i = tile.Object.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(tile.Object.transform.GetChild(i).gameObject);
            }

            SetTileBaseColor(tile);

            if (tile.CrateHealth > 0)
            {
                AddCrateOverlay(tile);
                return;
            }

            switch (tile.Special)
            {
                case SpecialKind.LineHorizontal:
                    AddTileIcon(tile, "LineHorizontalIcon", lineHorizontalIcon);
                    break;
                case SpecialKind.LineVertical:
                    AddTileIcon(tile, "LineVerticalIcon", lineVerticalIcon);
                    break;
                case SpecialKind.Bomb:
                    AddTileIcon(tile, "BombIcon", bombIcon);
                    break;
                case SpecialKind.Rainbow:
                    AddTileIcon(tile, "RainbowIcon", rainbowIcon);
                    break;
                case SpecialKind.Propeller:
                    AddTileIcon(tile, "PropellerIcon", propellerIcon);
                    break;
            }
        }

        private void AddCrateOverlay(Tile tile)
        {
            AddTileIcon(tile, "CrateIcon", crateTexture);
            if (tile.CrateHealth > 1)
            {
                AddTileMark(tile, "CrateLayer", new Color(0.98f, 0.78f, 0.24f), new Vector3(0.18f, 0.18f, 1f), new Vector3(0.22f, 0.22f, 0f));
            }

            if (tile.CrateHealth > 2)
            {
                AddTileMark(tile, "CrateHeavyLayer", new Color(0.74f, 0.82f, 0.92f), new Vector3(0.16f, 0.16f, 1f), new Vector3(-0.22f, 0.22f, 0f));
            }
        }

        private void AddTileIcon(Tile tile, string name, Texture2D icon)
        {
            if (icon == null)
            {
                AddTileMark(tile, $"{name}Fallback", Color.white, new Vector3(0.58f, 0.58f, 1f), Vector3.zero);
                return;
            }

            var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = name;
            mark.transform.SetParent(tile.Object.transform);
            mark.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            mark.transform.localScale = new Vector3(0.54f, 0.54f, 1f);
            var collider = mark.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = mark.GetComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.mainTexture = icon;
            renderer.material.color = Color.white;
        }

        private void AddTileMark(Tile tile, string name, Color color, Vector3 scale, Vector3 localPosition)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = name;
            mark.transform.SetParent(tile.Object.transform);
            mark.transform.localPosition = localPosition + new Vector3(0f, 0f, -0.05f);
            mark.transform.localScale = scale;
            var collider = mark.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = mark.GetComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.material.color = color;
        }

        private void ClearTiles()
        {
            CancelHint();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y]?.Object != null)
                    {
                        Destroy(board[x, y].Object);
                    }

                    board[x, y] = null;
                }
            }
        }

        private void TrySelectTile(Vector2 screenPosition)
        {
            if (IsPointerOverUi())
            {
                return;
            }

            var world = mainCamera.ScreenToWorldPoint(screenPosition);
            var grid = WorldToGrid(world);
            if (!IsInside(grid))
            {
                return;
            }

            if (!IsSelectableTile(grid))
            {
                if (selected.HasValue)
                {
                    Deselect(selected.Value);
                    selected = null;
                }

                return;
            }

            if (!selected.HasValue)
            {
                Select(grid);
                return;
            }

            var from = selected.Value;
            if (from == grid)
            {
                if (IsSpecialTile(grid) && Time.unscaledTime - lastClickTime <= 0.32f)
                {
                    Deselect(from);
                    TryActivateSpecialAt(grid);
                    selected = null;
                    return;
                }

                Deselect(from);
                selected = null;
                return;
            }

            if (CanSwapTiles(from, grid))
            {
                Deselect(from);
                TrySwap(from, grid);
                selected = null;
            }
            else
            {
                Deselect(from);
                Select(grid);
            }
        }

        private bool CanSwapTiles(Vector2Int from, Vector2Int to)
        {
            return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y) == 1 &&
                   IsSelectableTile(from) &&
                   IsSelectableTile(to);
        }

        private bool IsSelectableTile(Vector2Int grid)
        {
            return IsInside(grid) && board[grid.x, grid.y] != null && board[grid.x, grid.y].CrateHealth <= 0;
        }

        private void Select(Vector2Int grid)
        {
            selected = grid;
            lastClickTime = Time.unscaledTime;
            board[grid.x, grid.y].Object.transform.localScale = Vector3.one * (tileScale + 0.16f);
        }

        private void Deselect(Vector2Int grid)
        {
            if (IsInside(grid) && board[grid.x, grid.y] != null)
            {
                board[grid.x, grid.y].Object.transform.localScale = Vector3.one * tileScale;
            }
        }

        private void ClearSelectionIfAt(Vector2Int grid)
        {
            if (!selected.HasValue || selected.Value != grid)
            {
                return;
            }

            Deselect(grid);
            selected = null;
        }

        private void MarkEffectiveAction()
        {
            lastEffectiveActionTime = Time.unscaledTime;
            CancelHint();
        }

        private void TrySwap(Vector2Int a, Vector2Int b)
        {
            MarkEffectiveAction();
            SwapTiles(a, b);
            if (TryResolveSpecialSwap(a, b))
            {
                selected = null;
                return;
            }

            var matches = FindMatchGroups();
            if (matches.Count == 0)
            {
                SwapTiles(a, b);
                return;
            }

            movesRemaining = Mathf.Max(0, movesRemaining - 1);
            ResolveMatches(matches, GetPreferredSpecialSpawn(a, b, matches));
        }

        private bool TryActivateSpecialAt(Vector2Int pos)
        {
            if (!IsSpecialTile(pos))
            {
                return false;
            }

            inputLocked = true;
            comboChain++;
            movesRemaining = Mathf.Max(0, movesRemaining - 1);

            var clearSet = new HashSet<Vector2Int> { pos };
            if (board[pos.x, pos.y].Special == SpecialKind.Rainbow)
            {
                AddRainbowColorClear(GetMostCommonTileType(), clearSet);
                ApplyRainbowBonusClears(pos, clearSet);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
            return true;
        }

        private void SwapTiles(Vector2Int a, Vector2Int b)
        {
            (board[a.x, a.y], board[b.x, b.y]) = (board[b.x, b.y], board[a.x, a.y]);
            board[a.x, a.y].Object.transform.position = GridToWorld(a.x, a.y);
            board[b.x, b.y].Object.transform.position = GridToWorld(b.x, b.y);
        }

        private bool TryResolveSpecialSwap(Vector2Int a, Vector2Int b)
        {
            var first = board[a.x, a.y];
            var second = board[b.x, b.y];
            if (first == null || second == null)
            {
                return false;
            }

            if (first.Special == SpecialKind.None && second.Special == SpecialKind.None)
            {
                return false;
            }

            inputLocked = true;
            comboChain++;
            movesRemaining = Mathf.Max(0, movesRemaining - 1);

            if (first.Special != SpecialKind.None && second.Special != SpecialKind.None)
            {
                ResolveSpecialCombination(a, b, first.Special, second.Special);
                return true;
            }

            var specialPos = first.Special != SpecialKind.None ? a : b;
            var normalPos = first.Special != SpecialKind.None ? b : a;
            var clearSet = new HashSet<Vector2Int> { specialPos };
            var matches = FindMatchGroups();
            foreach (var matchPos in matches.SelectMany(group => group.Positions))
            {
                clearSet.Add(matchPos);
            }

            var specialToCreate = DetermineSpecialKind(matches, GetPreferredSpecialSpawn(a, b, matches));
            if (board[specialPos.x, specialPos.y].Special == SpecialKind.Rainbow)
            {
                AddRainbowColorClear(board[normalPos.x, normalPos.y].Type, clearSet);
                ApplyRainbowBonusClears(specialPos, clearSet);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, specialToCreate);
            return true;
        }

        private void ResolveSpecialCombination(Vector2Int a, Vector2Int b, SpecialKind firstSpecial, SpecialKind secondSpecial)
        {
            var clearSet = new HashSet<Vector2Int> { a, b };
            var firstIsRocket = IsRocket(firstSpecial);
            var secondIsRocket = IsRocket(secondSpecial);

            if (firstSpecial == SpecialKind.Rainbow && secondSpecial == SpecialKind.Rainbow)
            {
                AddEntireBoard(clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            if (firstSpecial == SpecialKind.Rainbow && firstSpecial != secondSpecial)
            {
                ResolveRainbowSpecialCombination(secondSpecial, clearSet);
                return;
            }

            if (secondSpecial == SpecialKind.Rainbow && firstSpecial != secondSpecial)
            {
                ResolveRainbowSpecialCombination(firstSpecial, clearSet);
                return;
            }

            if (firstSpecial == SpecialKind.Propeller || secondSpecial == SpecialKind.Propeller)
            {
                var propellerPos = secondSpecial == SpecialKind.Propeller ? b : a;
                var partnerPos = propellerPos == a ? b : a;
                var partnerSpecial = propellerPos == a ? secondSpecial : firstSpecial;
                ResolvePropellerCombination(propellerPos, partnerPos, partnerSpecial, clearSet);
                return;
            }

            if (firstIsRocket && secondIsRocket)
            {
                AddRow(b.y, clearSet);
                AddColumn(b.x, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            if (firstSpecial == SpecialKind.Bomb && secondSpecial == SpecialKind.Bomb)
            {
                AddRadius(b, 3, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            if ((firstIsRocket && secondSpecial == SpecialKind.Bomb) || (secondIsRocket && firstSpecial == SpecialKind.Bomb))
            {
                AddStrongRocketBombClear(b, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
        }

        private void ResolvePropellerCombination(Vector2Int propellerPos, Vector2Int partnerPos, SpecialKind partnerSpecial, HashSet<Vector2Int> clearSet)
        {
            var reservedTargets = new HashSet<Vector2Int> { propellerPos, partnerPos };
            var mode = GetPropellerTargetMode(partnerSpecial);
            var target = GetSmartPropellerTarget(mode, reservedTargets);
            reservedTargets.Add(target);
            AddCross(propellerPos, clearSet);

            if (partnerSpecial == SpecialKind.Bomb)
            {
                AddBombClear(target, clearSet);
            }
            else if (IsRocket(partnerSpecial))
            {
                if (partnerSpecial == SpecialKind.LineHorizontal)
                {
                    AddRow(target.y, clearSet);
                }
                else
                {
                    AddColumn(target.x, clearSet);
                }
            }
            else if (partnerSpecial == SpecialKind.Propeller)
            {
                for (var i = 0; i < 3; i++)
                {
                    var multiTarget = GetSmartPropellerTarget(PropellerTargetMode.Multi, reservedTargets);
                    reservedTargets.Add(multiTarget);
                    clearSet.Add(multiTarget);
                }
            }
            else
            {
                clearSet.Add(target);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null, false);
        }

        private void ResolveRainbowSpecialCombination(SpecialKind targetSpecial, HashSet<Vector2Int> clearSet)
        {
            var targetType = GetMostCommonTileType();
            var maxCount = targetSpecial == SpecialKind.Bomb ? 9 : IsRocket(targetSpecial) ? 14 : int.MaxValue;
            var positions = GetPrioritizedTilesOfType(targetType, maxCount);
            var specialToApply = IsRocket(targetSpecial)
                ? (rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical)
                : targetSpecial;

            foreach (var pos in positions)
            {
                if (board[pos.x, pos.y] == null)
                {
                    continue;
                }

                board[pos.x, pos.y].Special = specialToApply;
                DecorateTile(board[pos.x, pos.y]);
                if (targetSpecial == SpecialKind.Bomb)
                {
                    AddRadius(pos, 2, clearSet);
                }
                else if (IsRocket(targetSpecial))
                {
                    if (specialToApply == SpecialKind.LineHorizontal)
                    {
                        AddRow(pos.y, clearSet);
                    }
                    else
                    {
                        AddColumn(pos.x, clearSet);
                    }
                }
                else
                {
                    clearSet.Add(pos);
                }
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null, false);
        }

        private PropellerTargetMode GetPropellerTargetMode(SpecialKind partnerSpecial)
        {
            if (partnerSpecial == SpecialKind.Bomb)
            {
                return PropellerTargetMode.BombCarrier;
            }

            if (partnerSpecial == SpecialKind.LineHorizontal)
            {
                return PropellerTargetMode.RocketCarrierHorizontal;
            }

            if (partnerSpecial == SpecialKind.LineVertical)
            {
                return PropellerTargetMode.RocketCarrierVertical;
            }

            return partnerSpecial == SpecialKind.Propeller ? PropellerTargetMode.Multi : PropellerTargetMode.Single;
        }

        private Vector2Int GetSmartPropellerTarget(PropellerTargetMode mode, HashSet<Vector2Int> reserved)
        {
            var bestScore = float.MinValue;
            var bestTargets = new List<Vector2Int>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    if (board[x, y] == null)
                    {
                        continue;
                    }

                    var score = ScorePropellerTarget(pos, mode, reserved);
                    if (score > bestScore + 0.01f)
                    {
                        bestScore = score;
                        bestTargets.Clear();
                        bestTargets.Add(pos);
                    }
                    else if (Mathf.Abs(score - bestScore) <= 0.01f)
                    {
                        bestTargets.Add(pos);
                    }
                }
            }

            return bestTargets.Count == 0 ? new Vector2Int(rng.Next(Width), rng.Next(Height)) : bestTargets[rng.Next(bestTargets.Count)];
        }

        private float ScorePropellerTarget(Vector2Int target, PropellerTargetMode mode, HashSet<Vector2Int> reserved)
        {
            var tile = board[target.x, target.y];
            var score = IsColorTile(tile) ? 25f : -60f;
            if (tile != null && tile.CrateHealth > 0)
            {
                score = 120f + tile.CrateHealth * 40f;
            }

            if (reserved != null)
            {
                if (reserved.Contains(target))
                {
                    score -= 90f;
                }

                foreach (var reservedPos in reserved)
                {
                    var distance = Mathf.Abs(reservedPos.x - target.x) + Mathf.Abs(reservedPos.y - target.y);
                    if (distance <= 1)
                    {
                        score -= 24f;
                    }
                }
            }

            if (target.x == 0 || target.x == Width - 1)
            {
                score += 6f;
            }

            if (target.y == 0 || target.y == Height - 1)
            {
                score += 6f;
            }

            score += CountAdjacentSameColor(target) <= 1 ? 8f : 0f;
            var affected = GetPropellerAffectedArea(target, mode);
            foreach (var pos in affected)
            {
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                var special = board[pos.x, pos.y].Special;
                if (special == SpecialKind.Rainbow)
                {
                    score -= 140f;
                }
                else if (special == SpecialKind.Bomb)
                {
                    score -= mode == PropellerTargetMode.BombCarrier ? 120f : 85f;
                }
                else if (IsRocket(special) || special == SpecialKind.Propeller)
                {
                    score -= mode == PropellerTargetMode.Single ? 35f : 65f;
                }
                else if (special == SpecialKind.None)
                {
                    score += 3f;
                }
            }

            return score + (float)rng.NextDouble() * 2f;
        }

        private HashSet<Vector2Int> GetPropellerAffectedArea(Vector2Int target, PropellerTargetMode mode)
        {
            var affected = new HashSet<Vector2Int> { target };
            switch (mode)
            {
                case PropellerTargetMode.BombCarrier:
                    AddBombClearPreview(target, affected);
                    break;
                case PropellerTargetMode.RocketCarrierHorizontal:
                    AddRow(target.y, affected);
                    break;
                case PropellerTargetMode.RocketCarrierVertical:
                    AddColumn(target.x, affected);
                    break;
                case PropellerTargetMode.Single:
                case PropellerTargetMode.Multi:
                    break;
            }

            return affected;
        }

        private void AddBombClearPreview(Vector2Int target, HashSet<Vector2Int> affected)
        {
            var radius = HasUpgrade(UpgradeKind.ExplosionCore) ? 2 : 1;
            radius += Mathf.Min(2, GetUpgradeLevel(UpgradeKind.BombRadius));
            if (GetUpgradeLevel(UpgradeKind.ExplosionAftershock) > 0)
            {
                radius += 1;
            }

            AddRadius(target, radius, affected);
        }

        private int CountAdjacentSameColor(Vector2Int target)
        {
            if (!IsColorTile(board[target.x, target.y]))
            {
                return 0;
            }

            var count = 0;
            var offsets = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            foreach (var offset in offsets)
            {
                var pos = target + offset;
                if (IsInside(pos) && HasSameMatchColor(board[target.x, target.y], board[pos.x, pos.y]))
                {
                    count++;
                }
            }

            return count;
        }

        private int GetMostCommonTileType()
        {
            return Enumerable.Range(0, TileTypes)
                .OrderByDescending(type => GetTilesOfType(type).Count)
                .ThenBy(_ => rng.Next())
                .First();
        }

        private List<Vector2Int> GetTilesOfType(int targetType)
        {
            var result = new List<Vector2Int>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (IsColorTile(board[x, y]) && board[x, y].Type == targetType)
                    {
                        result.Add(new Vector2Int(x, y));
                    }
                }
            }

            return result;
        }

        private List<Vector2Int> GetPrioritizedTilesOfType(int targetType, int maxCount)
        {
            var candidates = GetTilesOfType(targetType)
                .OrderByDescending(GetAdjacentCratePressure)
                .ThenBy(_ => rng.Next())
                .ToList();

            return maxCount == int.MaxValue ? candidates : candidates.Take(maxCount).ToList();
        }

        private int GetAdjacentCratePressure(Vector2Int pos)
        {
            var pressure = 0;
            var offsets = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            foreach (var offset in offsets)
            {
                var check = pos + offset;
                if (IsInside(check) && board[check.x, check.y] != null && board[check.x, check.y].CrateHealth > 0)
                {
                    pressure += 1 + board[check.x, check.y].CrateHealth;
                }
            }

            return pressure;
        }

        private void AddTilesOfType(int targetType, HashSet<Vector2Int> output)
        {
            foreach (var pos in GetTilesOfType(targetType))
            {
                output.Add(pos);
            }
        }

        private void AddRainbowColorClear(int targetType, HashSet<Vector2Int> output)
        {
            var positions = GetTilesOfType(targetType);
            var count = HasUpgrade(UpgradeKind.RainbowCore)
                ? positions.Count
                : Mathf.CeilToInt(positions.Count * 0.5f);

            foreach (var pos in positions
                         .OrderByDescending(GetAdjacentCratePressure)
                         .ThenBy(_ => rng.Next())
                         .Take(count))
            {
                output.Add(pos);
            }
        }

        private IEnumerator EnsurePlayableBoardRoutine()
        {
            if (HasAvailableMove())
            {
                yield break;
            }

            ShowTriggerText("棋盘重排！", Color.white);
            var moves = ShuffleColorTiles();
            if (moves.Count > 0)
            {
                yield return AnimateFalls(moves);
                yield return new WaitForSeconds(CascadePauseSeconds);
            }
        }

        private void EnsurePlayableBoard()
        {
            for (var attempts = 0; attempts < 8 && !HasAvailableMove(); attempts++)
            {
                ShuffleColorTiles();
            }
        }

        private HintMove? FindBestHintMove()
        {
            HintMove? best = null;
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    if (!IsSelectableTile(pos))
                    {
                        continue;
                    }

                    TryScoreHintMove(pos, new Vector2Int(x + 1, y), ref best);
                    TryScoreHintMove(pos, new Vector2Int(x, y + 1), ref best);
                }
            }

            return best;
        }

        private void TryScoreHintMove(Vector2Int a, Vector2Int b, ref HintMove? best)
        {
            if (!IsSelectableTile(b))
            {
                return;
            }

            var score = ScoreHintMove(a, b);
            if (score <= 0f)
            {
                return;
            }

            if (!best.HasValue || score > best.Value.Score)
            {
                best = new HintMove(a, b, score);
            }
        }

        private float ScoreHintMove(Vector2Int a, Vector2Int b)
        {
            var first = board[a.x, a.y];
            var second = board[b.x, b.y];
            if (first == null || second == null)
            {
                return 0f;
            }

            if (first.Special != SpecialKind.None || second.Special != SpecialKind.None)
            {
                return ScoreSpecialHintMove(a, b, first.Special, second.Special);
            }

            SwapBoardReferences(a, b);
            var matches = FindMatchGroups();
            var matchedPositions = new HashSet<Vector2Int>(matches.SelectMany(group => group.Positions));
            var special = DetermineSpecialKind(matches, GetPreferredSpecialSpawn(a, b, matches));
            SwapBoardReferences(a, b);

            if (matches.Count == 0)
            {
                return 0f;
            }

            var crateHits = CountCratesAffectedByMatches(matchedPositions);
            var score = 10f + matchedPositions.Count;
            if (crateHits > 0)
            {
                score += 1000f + crateHits * 120f;
            }

            if (special.HasValue)
            {
                score += GetSpecialHintValue(special.Value.Special);
                score += GetBuildHintBonus(special.Value.Special);
                score += CountCratesAffectedBySpecialPreview(special.Value.Position, special.Value.Special) * 50f;
            }

            return score;
        }

        private float ScoreSpecialHintMove(Vector2Int a, Vector2Int b, SpecialKind firstSpecial, SpecialKind secondSpecial)
        {
            var score = 700f;
            if (firstSpecial != SpecialKind.None && secondSpecial != SpecialKind.None)
            {
                score += 500f;
            }

            score += GetSpecialHintValue(firstSpecial) + GetSpecialHintValue(secondSpecial);
            score += GetBuildHintBonus(firstSpecial) + GetBuildHintBonus(secondSpecial);
            score += CountCratesAffectedBySpecialPreview(a, firstSpecial) * 140f;
            score += CountCratesAffectedBySpecialPreview(b, secondSpecial) * 140f;
            return score;
        }

        private int CountCratesAffectedByMatches(HashSet<Vector2Int> matchedPositions)
        {
            var crateHits = new HashSet<Vector2Int>();
            foreach (var pos in matchedPositions)
            {
                AddAdjacentCrates(pos, crateHits);
            }

            return crateHits.Count;
        }

        private int CountCratesAffectedBySpecialPreview(Vector2Int pos, SpecialKind special)
        {
            if (special == SpecialKind.None)
            {
                return 0;
            }

            var affected = new HashSet<Vector2Int> { pos };
            if (special == SpecialKind.Bomb)
            {
                AddBombClearPreview(pos, affected);
            }
            else if (special == SpecialKind.LineHorizontal)
            {
                if (HasUpgrade(UpgradeKind.RocketCore))
                {
                    AddRow(pos.y, affected);
                }
                else
                {
                    AddLineSegment(pos, MatchOrientation.Horizontal, 3, affected);
                }
            }
            else if (special == SpecialKind.LineVertical)
            {
                if (HasUpgrade(UpgradeKind.RocketCore))
                {
                    AddColumn(pos.x, affected);
                }
                else
                {
                    AddLineSegment(pos, MatchOrientation.Vertical, 4, affected);
                }
            }
            else if (special == SpecialKind.Propeller)
            {
                var target = GetSmartPropellerTarget(PropellerTargetMode.Single, new HashSet<Vector2Int> { pos });
                affected.Add(target);
                AddCross(pos, affected);
            }

            return affected.Count(hit => IsInside(hit) && board[hit.x, hit.y] != null && board[hit.x, hit.y].CrateHealth > 0);
        }

        private float GetSpecialHintValue(SpecialKind special)
        {
            switch (special)
            {
                case SpecialKind.Rainbow:
                    return 360f;
                case SpecialKind.Bomb:
                    return 260f;
                case SpecialKind.LineHorizontal:
                case SpecialKind.LineVertical:
                    return 180f;
                case SpecialKind.Propeller:
                    return 160f;
                default:
                    return 0f;
            }
        }

        private float GetBuildHintBonus(SpecialKind special)
        {
            if (special == SpecialKind.Bomb && GetFactionInvestment(UpgradeFaction.Explosion) > 0)
            {
                return 160f + GetFactionInvestment(UpgradeFaction.Explosion) * 30f;
            }

            if (IsRocket(special) && GetFactionInvestment(UpgradeFaction.Rocket) > 0)
            {
                return 160f + GetFactionInvestment(UpgradeFaction.Rocket) * 30f;
            }

            if (special == SpecialKind.Rainbow && GetFactionInvestment(UpgradeFaction.Rainbow) > 0)
            {
                return 170f + GetFactionInvestment(UpgradeFaction.Rainbow) * 35f;
            }

            if (special == SpecialKind.Propeller && GetFactionInvestment(UpgradeFaction.Propeller) > 0)
            {
                return 130f + GetFactionInvestment(UpgradeFaction.Propeller) * 25f;
            }

            return 0f;
        }

        private bool HasAvailableMove()
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    if (!IsSelectableTile(pos))
                    {
                        continue;
                    }

                    var right = new Vector2Int(x + 1, y);
                    if (IsSelectableTile(right) && WouldSwapCreateMatch(pos, right))
                    {
                        return true;
                    }

                    var up = new Vector2Int(x, y + 1);
                    if (IsSelectableTile(up) && WouldSwapCreateMatch(pos, up))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool WouldSwapCreateMatch(Vector2Int a, Vector2Int b)
        {
            SwapBoardReferences(a, b);
            var createsMatch = IsPartOfMatch(a) || IsPartOfMatch(b);
            SwapBoardReferences(a, b);
            return createsMatch;
        }

        private void SwapBoardReferences(Vector2Int a, Vector2Int b)
        {
            (board[a.x, a.y], board[b.x, b.y]) = (board[b.x, b.y], board[a.x, a.y]);
        }

        private bool IsPartOfMatch(Vector2Int pos)
        {
            return CountLineMatch(pos, Vector2Int.left, Vector2Int.right) >= 3 ||
                   CountLineMatch(pos, Vector2Int.down, Vector2Int.up) >= 3;
        }

        private int CountLineMatch(Vector2Int origin, Vector2Int negativeDirection, Vector2Int positiveDirection)
        {
            if (!IsInside(origin) || !IsColorTile(board[origin.x, origin.y]))
            {
                return 0;
            }

            return 1 + CountSameColorInDirection(origin, negativeDirection) + CountSameColorInDirection(origin, positiveDirection);
        }

        private int CountSameColorInDirection(Vector2Int origin, Vector2Int direction)
        {
            var count = 0;
            var current = origin + direction;
            while (IsInside(current) && HasSameMatchColor(board[origin.x, origin.y], board[current.x, current.y]))
            {
                count++;
                current += direction;
            }

            return count;
        }

        private List<TileMove> ShuffleColorTiles()
        {
            var positions = new List<Vector2Int>();
            var tiles = new List<Tile>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (IsColorTile(board[x, y]))
                    {
                        positions.Add(new Vector2Int(x, y));
                        tiles.Add(board[x, y]);
                    }
                }
            }

            if (positions.Count <= 1)
            {
                return new List<TileMove>();
            }

            var originalPositions = tiles.ToDictionary(tile => tile, tile => tile.Object.transform.position);
            for (var attempts = 0; attempts < 20; attempts++)
            {
                var shuffledTiles = tiles.OrderBy(_ => rng.Next()).ToList();
                for (var i = 0; i < positions.Count; i++)
                {
                    var pos = positions[i];
                    board[pos.x, pos.y] = shuffledTiles[i];
                }

                if (HasAvailableMove() || attempts == 19)
                {
                    var moves = new List<TileMove>();
                    foreach (var pos in positions)
                    {
                        var tile = board[pos.x, pos.y];
                        var to = GridToWorld(pos.x, pos.y);
                        var from = originalPositions[tile];
                        tile.Object.transform.position = from;
                        moves.Add(new TileMove(tile, from, to, Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to)))));
                    }

                    return moves;
                }
            }

            return new List<TileMove>();
        }

        private void UpdateIdleHint()
        {
            if (upgradePanelOpen || remainingCrates <= 0)
            {
                CancelHint();
                lastEffectiveActionTime = Time.unscaledTime;
                return;
            }

            var idleSeconds = Time.unscaledTime - lastEffectiveActionTime;
            if (idleSeconds < HintDelaySeconds)
            {
                CancelHint();
                return;
            }

            if (!HasAvailableMove())
            {
                CancelHint();
                StartCoroutine(EnsurePlayableBoardRoutine());
                lastEffectiveActionTime = Time.unscaledTime;
                return;
            }

            if (!hintFrom.HasValue || !hintTo.HasValue)
            {
                var hint = FindBestHintMove();
                if (!hint.HasValue)
                {
                    return;
                }

                hintFrom = hint.Value.From;
                hintTo = hint.Value.To;
            }

            AnimateHint(idleSeconds >= StrongHintDelaySeconds);
        }

        private void AnimateHint(bool strong)
        {
            if (!hintFrom.HasValue || !hintTo.HasValue)
            {
                return;
            }

            var pulse = (Mathf.Sin(Time.unscaledTime * (strong ? 5.2f : 3.2f)) + 1f) * 0.5f;
            var scale = tileScale + Mathf.Lerp(strong ? 0.08f : 0.035f, strong ? 0.18f : 0.10f, pulse);
            ApplyHintScale(hintFrom.Value, scale);
            ApplyHintScale(hintTo.Value, scale);

            if (strong)
            {
                ShowHintArrow();
            }
            else if (hintArrow != null)
            {
                hintArrow.SetActive(false);
            }
        }

        private void ApplyHintScale(Vector2Int pos, float scale)
        {
            if (IsInside(pos) && board[pos.x, pos.y] != null)
            {
                board[pos.x, pos.y].Object.transform.localScale = Vector3.one * scale;
            }
        }

        private void ShowHintArrow()
        {
            if (!hintFrom.HasValue || !hintTo.HasValue)
            {
                return;
            }

            if (hintArrow == null)
            {
                hintArrow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                hintArrow.name = "Hint Arrow";
                var collider = hintArrow.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                var renderer = hintArrow.GetComponent<MeshRenderer>();
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.material.color = new Color(1f, 0.92f, 0.28f, 0.88f);
            }

            var from = GridToWorld(hintFrom.Value.x, hintFrom.Value.y);
            var to = GridToWorld(hintTo.Value.x, hintTo.Value.y);
            var middle = (from + to) * 0.5f + new Vector3(0f, 0f, -0.35f);
            hintArrow.SetActive(true);
            hintArrow.transform.position = middle;
            hintArrow.transform.localScale = new Vector3(0.52f, 0.12f, 1f);
            var delta = to - from;
            hintArrow.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void CancelHint()
        {
            if (hintFrom.HasValue)
            {
                ResetHintScale(hintFrom.Value);
            }

            if (hintTo.HasValue)
            {
                ResetHintScale(hintTo.Value);
            }

            hintFrom = null;
            hintTo = null;
            if (hintArrow != null)
            {
                hintArrow.SetActive(false);
            }
        }

        private void ResetHintScale(Vector2Int pos)
        {
            if (selected.HasValue && selected.Value == pos)
            {
                return;
            }

            if (IsInside(pos) && board[pos.x, pos.y] != null)
            {
                board[pos.x, pos.y].Object.transform.localScale = Vector3.one * tileScale;
            }
        }

        private void ResolveMatches(List<MatchGroup> matchGroups, Vector2Int? specialSpawn)
        {
            MarkEffectiveAction();
            inputLocked = true;
            comboChain++;

            var matchedPositions = new HashSet<Vector2Int>(matchGroups.SelectMany(group => group.Positions));
            var specialToCreate = DetermineSpecialKind(matchGroups, specialSpawn);
            AwardScoreForClears(matchedPositions.Count);

            ResolveClearSet(matchedPositions, specialToCreate);
        }

        private void AwardScoreForClears(int clearCount)
        {
            var scoreGain = clearCount * 80;
            scoreGain += comboChain > 1 ? comboChain * 40 : 0;
            score += scoreGain;
            runScore += scoreGain;
            bestComboChain = Mathf.Max(bestComboChain, comboChain);
        }

        private void ResolveClearSet(HashSet<Vector2Int> baseClears, PendingSpecial? specialToCreate, bool expandSpecials = true)
        {
            StartCoroutine(ResolveClearSetRoutine(baseClears, specialToCreate, expandSpecials));
        }

        private IEnumerator ResolveClearSetRoutine(HashSet<Vector2Int> baseClears, PendingSpecial? specialToCreate, bool expandSpecials = true)
        {
            var currentClears = baseClears;
            var currentSpecial = specialToCreate;
            var currentExpandSpecials = expandSpecials;

            while (true)
            {
                yield return new WaitForSeconds(MatchPauseSeconds);

                var bonusClears = new HashSet<Vector2Int>(currentClears);
                if (currentExpandSpecials)
                {
                    ExpandSpecialClears(currentClears, bonusClears);
                }

                if (currentSpecial.HasValue)
                {
                    bonusClears.Remove(currentSpecial.Value.Position);
                }

                DamageCratesForClears(currentClears, bonusClears);
                yield return AnimateAndRemoveClears(bonusClears);

                if (currentSpecial.HasValue)
                {
                    CreateSpecialTileAt(currentSpecial.Value);
                }

                ApplyPostClearUpgradeSpawns(bonusClears);
                ApplyDeterministicPostClearSpawns();

                var fallMoves = ApplyGravity();
                var spawnMoves = RefillBoard();
                fallMoves.AddRange(spawnMoves);
                yield return AnimateFalls(fallMoves);
                yield return new WaitForSeconds(CascadePauseSeconds);
                yield return EnsurePlayableBoardRoutine();

                var cascades = FindMatchGroups();
                if (cascades.Count == 0)
                {
                    break;
                }

                comboChain++;
                currentClears = new HashSet<Vector2Int>(cascades.SelectMany(group => group.Positions));
                currentSpecial = DetermineSpecialKind(cascades, GetPreferredSpecialSpawn(null, null, cascades));
                currentExpandSpecials = true;
                AwardScoreForClears(currentClears.Count);
            }

            comboChain = 0;
            inputLocked = false;
            MarkEffectiveAction();

            if (remainingCrates <= 0)
            {
                CompleteRoom();
                yield break;
            }

            if (movesRemaining <= 0)
            {
                FailRun();
            }
        }

        private IEnumerator AnimateAndRemoveClears(HashSet<Vector2Int> clears)
        {
            var clearedTiles = new List<Tile>();
            foreach (var pos in clears)
            {
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                ClearSelectionIfAt(pos);
                clearedTiles.Add(board[pos.x, pos.y]);
                board[pos.x, pos.y] = null;
            }

            for (var elapsed = 0f; elapsed < ClearAnimSeconds; elapsed += Time.deltaTime)
            {
                var t = Mathf.Clamp01(elapsed / ClearAnimSeconds);
                var scale = Mathf.Lerp(tileScale * 1.12f, 0.08f, EaseInBack(t));
            var flash = Mathf.Sin(t * Mathf.PI);
            foreach (var tile in clearedTiles)
            {
                    if (tile.Object == null)
                    {
                        continue;
                    }

                    tile.Object.transform.localScale = Vector3.one * scale;
                    var renderer = tile.Object.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = Color.Lerp(GetTileDisplayColor(tile), Color.white, flash * 0.75f);
                    }
                }

                yield return null;
            }

            foreach (var tile in clearedTiles)
            {
                if (tile.Object != null)
                {
                    Destroy(tile.Object);
                }
            }
        }

        private void DamageCratesForClears(HashSet<Vector2Int> directClears, HashSet<Vector2Int> finalClears)
        {
            var crateHits = new HashSet<Vector2Int>();
            foreach (var pos in finalClears)
            {
                crateHits.Add(pos);
            }

            foreach (var pos in directClears)
            {
                AddAdjacentCrates(pos, crateHits);
            }

            foreach (var pos in crateHits)
            {
                if (!IsInside(pos) || board[pos.x, pos.y] == null || board[pos.x, pos.y].CrateHealth <= 0)
                {
                    continue;
                }

                var wasScheduledToClear = finalClears.Contains(pos);
                var destroyed = DamageCrate(pos);
                if (!destroyed)
                {
                    finalClears.Remove(pos);
                }
                else if (!wasScheduledToClear && board[pos.x, pos.y] != null)
                {
                    DecorateTile(board[pos.x, pos.y]);
                }
            }
        }

        private void AddAdjacentCrates(Vector2Int center, HashSet<Vector2Int> output)
        {
            var offsets = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            foreach (var offset in offsets)
            {
                var pos = center + offset;
                if (IsInside(pos) && board[pos.x, pos.y] != null && board[pos.x, pos.y].CrateHealth > 0)
                {
                    output.Add(pos);
                }
            }
        }

        private bool DamageCrate(Vector2Int pos)
        {
            ClearSelectionIfAt(pos);
            var tile = board[pos.x, pos.y];
            tile.CrateHealth--;
            if (tile.CrateHealth <= 0)
            {
                tile.Object.transform.localScale = Vector3.one * tileScale;
                remainingCrates = Mathf.Max(0, remainingCrates - 1);
                StartCoroutine(AnimateCrateBreak(tile));
                return true;
            }

            StartCoroutine(AnimateCrateHit(tile));
            DecorateTile(tile);
            return false;
        }

        private IEnumerator AnimateCrateHit(Tile tile)
        {
            if (tile?.Object == null)
            {
                yield break;
            }

            var original = tile.Object.transform.localScale;
            tile.Object.transform.localScale = original * 1.12f;
            yield return new WaitForSeconds(0.06f);
            if (tile.Object != null)
            {
                tile.Object.transform.localScale = original;
            }
        }

        private IEnumerator AnimateCrateBreak(Tile tile)
        {
            if (tile?.Object == null)
            {
                yield break;
            }

            tile.Object.transform.localScale = Vector3.one * (tileScale * 1.2f);
            yield return new WaitForSeconds(0.05f);
            if (tile.Object != null)
            {
                tile.Object.transform.localScale = Vector3.one * tileScale;
            }
        }

        private void ShowTriggerText(string message, Color color)
        {
            lastEffectiveActionTime = Time.unscaledTime;
            CancelHint();
            if (triggerTextRoutine != null)
            {
                StopCoroutine(triggerTextRoutine);
            }

            triggerTextRoutine = StartCoroutine(ShowTriggerTextRoutine(message, color));
        }

        private void AddNeighbors(Vector2Int center, HashSet<Vector2Int> output)
        {
            AddRadius(center, 1, output);
        }

        private void AddCross(Vector2Int center, HashSet<Vector2Int> output)
        {
            output.Add(center);
            var offsets = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            foreach (var offset in offsets)
            {
                var pos = center + offset;
                if (IsInside(pos))
                {
                    output.Add(pos);
                }
            }
        }

        private void AddDiagonalCorners(Vector2Int center, HashSet<Vector2Int> output)
        {
            var offsets = new[]
            {
                new Vector2Int(1, 1),
                new Vector2Int(1, -1),
                new Vector2Int(-1, 1),
                new Vector2Int(-1, -1)
            };

            foreach (var offset in offsets)
            {
                var pos = center + offset;
                if (IsInside(pos))
                {
                    output.Add(pos);
                }
            }
        }

        private void AddRadius(Vector2Int center, int radius, HashSet<Vector2Int> output)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var pos = new Vector2Int(center.x + dx, center.y + dy);
                    if (IsInside(pos))
                    {
                        output.Add(pos);
                    }
                }
            }
        }

        private void AddRow(int y, HashSet<Vector2Int> output)
        {
            for (var x = 0; x < Width; x++)
            {
                output.Add(new Vector2Int(x, y));
            }
        }

        private void AddColumn(int x, HashSet<Vector2Int> output)
        {
            for (var y = 0; y < Height; y++)
            {
                output.Add(new Vector2Int(x, y));
            }
        }

        private void AddLineSegment(Vector2Int center, MatchOrientation orientation, int reach, HashSet<Vector2Int> output)
        {
            for (var offset = -reach; offset <= reach; offset++)
            {
                var pos = orientation == MatchOrientation.Horizontal
                    ? new Vector2Int(center.x + offset, center.y)
                    : new Vector2Int(center.x, center.y + offset);
                if (IsInside(pos))
                {
                    output.Add(pos);
                }
            }
        }

        private void AddWideCross(Vector2Int center, HashSet<Vector2Int> output)
        {
            for (var offset = -1; offset <= 1; offset++)
            {
                var row = Mathf.Clamp(center.y + offset, 0, Height - 1);
                var column = Mathf.Clamp(center.x + offset, 0, Width - 1);
                AddRow(row, output);
                AddColumn(column, output);
            }
        }

        private void AddStrongRocketBombClear(Vector2Int center, HashSet<Vector2Int> output)
        {
            AddRow(center.y, output);
            AddColumn(center.x, output);
            AddRadius(center, 2, output);
        }

        private void AddEntireBoard(HashSet<Vector2Int> output)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    output.Add(new Vector2Int(x, y));
                }
            }
        }

        private void ApplyPostClearUpgradeSpawns(HashSet<Vector2Int> clearedPositions)
        {
            var clearedCount = clearedPositions.Count;
            TryCreateSpecialByClearProgress(UpgradeKind.BombSpawn, ref bombSpawnClearProgress, clearedCount, 18, 15, 12, () => SpecialKind.Bomb);
            TryCreateSpecialByClearProgress(UpgradeKind.RocketSpawn, ref rocketSpawnClearProgress, clearedCount, 16, 13, 10, RollRocketSpecial);
            TryCreateSpecialByClearProgress(UpgradeKind.RainbowSpawn, ref rainbowSpawnClearProgress, clearedCount, 28, 24, 20, () => SpecialKind.Rainbow);
            TryCreateSpecialByClearProgress(UpgradeKind.PropellerSpawn, ref propellerSpawnClearProgress, clearedCount, 14, 12, 10, () => SpecialKind.Propeller);
        }

        private void ApplyDeterministicPostClearSpawns()
        {
            for (var i = 0; i < pendingRainbowCopySpawns; i++)
            {
                CreateSpecialOnRandomColorTile(SpecialKind.Rainbow);
            }

            for (var i = 0; i < pendingBridgeBombSpawns; i++)
            {
                CreateSpecialOnRandomColorTile(SpecialKind.Bomb);
            }

            for (var i = 0; i < pendingBridgeRocketSpawns; i++)
            {
                CreateSpecialOnRandomColorTile(rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical);
            }

            pendingRainbowCopySpawns = 0;
            pendingBridgeBombSpawns = 0;
            pendingBridgeRocketSpawns = 0;
        }

        private void TryCreateSpecialByClearProgress(UpgradeKind kind, ref int clearProgress, int clearedCount, int levelOneThreshold, int levelTwoThreshold, int levelThreeThreshold, Func<SpecialKind> specialFactory)
        {
            var level = GetUpgradeLevel(kind);
            if (level <= 0 || clearedCount <= 0)
            {
                return;
            }

            clearProgress += clearedCount;
            var threshold = GetClearThresholdByLevel(level, levelOneThreshold, levelTwoThreshold, levelThreeThreshold);
            var triggered = false;
            while (clearProgress >= threshold)
            {
                clearProgress -= threshold;
                if (!triggered)
                {
                    ShowUpgradeTrigger(GetUpgradeDefinition(kind));
                    triggered = true;
                }

                CreateSpecialOnRandomColorTile(specialFactory());
            }
        }

        private int GetClearThresholdByLevel(int level, int levelOne, int levelTwo, int levelThree)
        {
            if (level <= 1)
            {
                return levelOne;
            }

            if (level == 2)
            {
                return levelTwo;
            }

            return levelThree;
        }

        private SpecialKind RollRocketSpecial()
        {
            return rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical;
        }

        private void CreateSpecialOnRandomColorTile(SpecialKind special)
        {
            var candidates = GetSpecialSpawnCandidates(special);
            if (candidates.Count == 0)
            {
                return;
            }

            var pos = candidates[rng.Next(candidates.Count)];
            board[pos.x, pos.y].Special = special;
            DecorateTile(board[pos.x, pos.y]);
        }

        private List<Vector2Int> GetSpecialSpawnCandidates(SpecialKind special)
        {
            var candidates = new List<Vector2Int>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (IsColorTile(board[x, y]))
                    {
                        candidates.Add(new Vector2Int(x, y));
                    }
                }
            }

            if (special != SpecialKind.Rainbow)
            {
                return candidates;
            }

            var center = new Vector2(Width * 0.5f - 0.5f, Height * 0.5f - 0.5f);
            return candidates
                .OrderBy(pos => Vector2.Distance(new Vector2(pos.x, pos.y), center))
                .ThenByDescending(GetAdjacentCratePressure)
                .ThenBy(_ => rng.Next())
                .ToList();
        }

        private void SeedShowcaseSpecialsForRoom()
        {
            if (HasUpgrade(UpgradeKind.RainbowCore))
            {
                CreateSpecialOnRandomColorTile(SpecialKind.Rainbow);
            }

            if (room == 3)
            {
                CreateSpecialOnRandomColorTile(rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical);
                return;
            }

            if (room < 4)
            {
                return;
            }

            CreateSpecialOnRandomColorTile(SpecialKind.Bomb);
            CreateSpecialOnRandomColorTile(rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical);
            CreateSpecialOnRandomColorTile(SpecialKind.Propeller);
        }

        private void ExpandSpecialClears(HashSet<Vector2Int> baseClears, HashSet<Vector2Int> output)
        {
            var expandedSpecials = new HashSet<Vector2Int>();
            var queuedSpecials = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var pos in baseClears)
            {
                QueueSpecialExpansion(pos, expandedSpecials, queuedSpecials, queue);
            }

            while (queue.Count > 0)
            {
                var pos = queue.Dequeue();
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                if (!expandedSpecials.Add(pos))
                {
                    continue;
                }

                var tile = board[pos.x, pos.y];
                switch (tile.Special)
                {
                    case SpecialKind.LineHorizontal:
                        AddRocketClear(pos, MatchOrientation.Horizontal, output);
                        break;
                    case SpecialKind.LineVertical:
                        AddRocketClear(pos, MatchOrientation.Vertical, output);
                        break;
                    case SpecialKind.Bomb:
                        AddBombClear(pos, output);
                        break;
                    case SpecialKind.Rainbow:
                        AddRainbowColorClear(GetMostCommonTileType(), output);
                        ApplyRainbowBonusClears(pos, output);
                        break;
                    case SpecialKind.Propeller:
                        AddPropellerClear(pos, output);
                        break;
                }

                foreach (var next in output.ToArray())
                {
                    QueueSpecialExpansion(next, expandedSpecials, queuedSpecials, queue);
                }
            }
        }

        private void QueueSpecialExpansion(Vector2Int pos, HashSet<Vector2Int> expandedSpecials, HashSet<Vector2Int> queuedSpecials, Queue<Vector2Int> queue)
        {
            if (expandedSpecials.Contains(pos) || queuedSpecials.Contains(pos) || !IsSpecialTile(pos))
            {
                return;
            }

            queuedSpecials.Add(pos);
            queue.Enqueue(pos);
        }

        private void AddRocketClear(Vector2Int pos, MatchOrientation orientation, HashSet<Vector2Int> output)
        {
            rocketActivationCount++;
            if (HasUpgrade(UpgradeKind.RocketCore))
            {
                if (orientation == MatchOrientation.Horizontal)
                {
                    AddRow(pos.y, output);
                }
                else
                {
                    AddColumn(pos.x, output);
                }
            }
            else
            {
                var reach = orientation == MatchOrientation.Horizontal ? 3 : 4;
                AddLineSegment(pos, orientation, reach, output);
            }

            if (GetUpgradeLevel(UpgradeKind.RocketSplit) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RocketSplit));
                if (orientation == MatchOrientation.Horizontal)
                {
                    AddColumn(pos.x, output);
                }
                else
                {
                    AddRow(pos.y, output);
                }
            }

            var extraLevel = GetUpgradeLevel(UpgradeKind.RocketExtra);
            if (extraLevel > 0 && rocketActivationCount % 2 == 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RocketExtra));
                if (orientation == MatchOrientation.Horizontal)
                {
                    AddRow(Mathf.Clamp(pos.y + (rng.Next(2) == 0 ? -1 : 1), 0, Height - 1), output);
                }
                else
                {
                    AddColumn(Mathf.Clamp(pos.x + (rng.Next(2) == 0 ? -1 : 1), 0, Width - 1), output);
                }
            }

            if (GetUpgradeLevel(UpgradeKind.BridgeRocketBomb) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.BridgeRocketBomb));
                foreach (var hit in output.ToArray())
                {
                    if (IsInside(hit) && board[hit.x, hit.y] != null && board[hit.x, hit.y].Special == SpecialKind.Bomb)
                    {
                        AddBombClear(hit, output);
                    }
                }
            }
        }

        private void AddBombClear(Vector2Int pos, HashSet<Vector2Int> output)
        {
            var radius = HasUpgrade(UpgradeKind.ExplosionCore) ? 2 : 1;
            radius += Mathf.Min(2, GetUpgradeLevel(UpgradeKind.BombRadius));
            if (GetUpgradeLevel(UpgradeKind.BombRadius) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.BombRadius));
            }

            if (GetUpgradeLevel(UpgradeKind.ExplosionAftershock) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.ExplosionAftershock));
                radius += 1;
            }

            AddRadius(pos, radius, output);
        }

        private void ApplyRainbowBonusClears(Vector2Int pos, HashSet<Vector2Int> output)
        {
            rainbowActivationCount++;
            if (GetUpgradeLevel(UpgradeKind.RainbowChainBomb) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RainbowChainBomb));
                AddRadius(pos, 1 + GetUpgradeLevel(UpgradeKind.RainbowChainBomb), output);
            }

            if (GetUpgradeLevel(UpgradeKind.RainbowCopy) > 0 && rainbowActivationCount <= 2)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RainbowCopy));
                pendingRainbowCopySpawns++;
            }

            if (GetUpgradeLevel(UpgradeKind.BridgeRainbowBomb) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.BridgeRainbowBomb));
                var bomb = FindSpecialTile(SpecialKind.Bomb);
                if (bomb.HasValue)
                {
                    AddBombClear(bomb.Value, output);
                }
                else
                {
                    pendingBridgeBombSpawns++;
                }
            }

            if (GetUpgradeLevel(UpgradeKind.BridgeRainbowRocket) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.BridgeRainbowRocket));
                AddBestRocketLine(output);
                pendingBridgeRocketSpawns++;
            }
        }

        private void AddPropellerClear(Vector2Int pos, HashSet<Vector2Int> output)
        {
            propellerActivationCount++;
            var reservedTargets = new HashSet<Vector2Int>(output) { pos };
            var target = GetSmartPropellerTarget(PropellerTargetMode.Single, reservedTargets);
            reservedTargets.Add(target);
            AddCross(pos, output);
            output.Add(target);
            if (HasUpgrade(UpgradeKind.PropellerCore))
            {
                var coreTarget = GetSmartPropellerTarget(PropellerTargetMode.Multi, reservedTargets);
                reservedTargets.Add(coreTarget);
                output.Add(coreTarget);
            }

            if (GetUpgradeLevel(UpgradeKind.PropellerBlast) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.PropellerBlast));
                AddCross(target, output);
            }

            if (GetUpgradeLevel(UpgradeKind.PropellerSwarm) > 0 && propellerActivationCount % 3 == 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.PropellerSwarm));
                var swarmTarget = GetSmartPropellerTarget(PropellerTargetMode.Multi, reservedTargets);
                reservedTargets.Add(swarmTarget);
                output.Add(swarmTarget);
            }
        }

        private Vector2Int? FindSpecialTile(SpecialKind special)
        {
            var candidates = new List<Vector2Int>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y] != null && board[x, y].Special == special)
                    {
                        candidates.Add(new Vector2Int(x, y));
                    }
                }
            }

            return candidates.Count == 0 ? (Vector2Int?)null : candidates[rng.Next(candidates.Count)];
        }

        private void AddBestRocketLine(HashSet<Vector2Int> output)
        {
            var bestIsRow = true;
            var bestIndex = 0;
            var bestScore = -1;
            for (var y = 0; y < Height; y++)
            {
                var score = Enumerable.Range(0, Width).Count(x => board[x, y] != null);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIsRow = true;
                    bestIndex = y;
                }
            }

            for (var x = 0; x < Width; x++)
            {
                var score = Enumerable.Range(0, Height).Count(y => board[x, y] != null);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIsRow = false;
                    bestIndex = x;
                }
            }

            if (bestIsRow)
            {
                AddRow(bestIndex, output);
            }
            else
            {
                AddColumn(bestIndex, output);
            }
        }

        private List<MatchGroup> FindMatchGroups()
        {
            var result = new List<MatchGroup>();

            for (var y = 0; y < Height; y++)
            {
                var runStart = 0;
                for (var x = 1; x <= Width; x++)
                {
                    var same = x < Width && HasSameMatchColor(board[x, y], board[runStart, y]);
                    if (same)
                    {
                        continue;
                    }

                    var runLength = x - runStart;
                    if (runLength >= 3)
                    {
                        var positions = new List<Vector2Int>();
                        for (var i = runStart; i < x; i++)
                        {
                            positions.Add(new Vector2Int(i, y));
                        }

                        result.Add(new MatchGroup(positions, MatchOrientation.Horizontal));
                    }

                    runStart = x;
                }
            }

            for (var x = 0; x < Width; x++)
            {
                var runStart = 0;
                for (var y = 1; y <= Height; y++)
                {
                    var same = y < Height && HasSameMatchColor(board[x, y], board[x, runStart]);
                    if (same)
                    {
                        continue;
                    }

                    var runLength = y - runStart;
                    if (runLength >= 3)
                    {
                        var positions = new List<Vector2Int>();
                        for (var i = runStart; i < y; i++)
                        {
                            positions.Add(new Vector2Int(x, i));
                        }

                        result.Add(new MatchGroup(positions, MatchOrientation.Vertical));
                    }

                    runStart = y;
                }
            }

            for (var x = 0; x < Width - 1; x++)
            {
                for (var y = 0; y < Height - 1; y++)
                {
                    var first = board[x, y];
                    if (!IsColorTile(first))
                    {
                        continue;
                    }

                    if (HasSameMatchColor(first, board[x + 1, y]) &&
                        HasSameMatchColor(first, board[x, y + 1]) &&
                        HasSameMatchColor(first, board[x + 1, y + 1]))
                    {
                        result.Add(new MatchGroup(new List<Vector2Int>
                        {
                            new Vector2Int(x, y),
                            new Vector2Int(x + 1, y),
                            new Vector2Int(x, y + 1),
                            new Vector2Int(x + 1, y + 1)
                        }, MatchOrientation.Square));
                    }
                }
            }

            return result;
        }

        private Vector2Int? GetPreferredSpecialSpawn(Vector2Int? firstSwap, Vector2Int? secondSwap, List<MatchGroup> matchGroups)
        {
            if (matchGroups.Count == 0)
            {
                return null;
            }

            var matchedPositions = new HashSet<Vector2Int>(matchGroups.SelectMany(group => group.Positions));
            if (firstSwap.HasValue && matchedPositions.Contains(firstSwap.Value))
            {
                return firstSwap.Value;
            }

            if (secondSwap.HasValue && matchedPositions.Contains(secondSwap.Value))
            {
                return secondSwap.Value;
            }

            var intersections = matchGroups
                .SelectMany(group => group.Positions)
                .GroupBy(pos => pos)
                .FirstOrDefault(group => group.Count() > 1);
            if (intersections != null)
            {
                return intersections.Key;
            }

            return matchGroups.OrderByDescending(group => group.Positions.Count).First().Positions[0];
        }

        private PendingSpecial? DetermineSpecialKind(List<MatchGroup> matchGroups, Vector2Int? spawnPosition)
        {
            if (matchGroups.Count == 0)
            {
                return null;
            }

            var squareGroup = matchGroups.FirstOrDefault(group => group.Orientation == MatchOrientation.Square);
            if (squareGroup != null)
            {
                var matchedPositions = new HashSet<Vector2Int>(matchGroups.SelectMany(group => group.Positions));
                var squarePosition = spawnPosition.HasValue && matchedPositions.Contains(spawnPosition.Value)
                    ? spawnPosition.Value
                    : squareGroup.Positions[0];
                return new PendingSpecial(squarePosition, SpecialKind.Propeller);
            }

            var intersection = matchGroups
                .SelectMany(group => group.Positions)
                .GroupBy(pos => pos)
                .FirstOrDefault(group => group.Count() > 1);
            if (intersection != null)
            {
                return new PendingSpecial(intersection.Key, SpecialKind.Bomb);
            }

            var strongestGroup = matchGroups.OrderByDescending(group => group.Positions.Count).First();
            var position = spawnPosition.HasValue && strongestGroup.Positions.Contains(spawnPosition.Value)
                ? spawnPosition.Value
                : strongestGroup.Positions[0];

            if (strongestGroup.Positions.Count >= 5)
            {
                return new PendingSpecial(position, SpecialKind.Rainbow);
            }

            if (strongestGroup.Positions.Count == 4)
            {
                var specialKind = strongestGroup.Orientation == MatchOrientation.Horizontal
                    ? SpecialKind.LineVertical
                    : SpecialKind.LineHorizontal;
                return new PendingSpecial(position, specialKind);
            }

            return null;
        }

        private void CreateSpecialTileAt(PendingSpecial pendingSpecial)
        {
            var pos = pendingSpecial.Position;
            if (!IsInside(pos))
            {
                return;
            }

            if (board[pos.x, pos.y] == null)
            {
                board[pos.x, pos.y] = CreateTile(pos.x, pos.y, rng.Next(TileTypes));
            }

            board[pos.x, pos.y].Special = pendingSpecial.Special;
            board[pos.x, pos.y].Object.transform.position = GridToWorld(pos.x, pos.y);
            board[pos.x, pos.y].Object.transform.localScale = Vector3.one * tileScale;
            DecorateTile(board[pos.x, pos.y]);
        }

        private void SetTileBaseColor(Tile tile)
        {
            var renderer = tile.Object.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            if (tile.CrateHealth > 0)
            {
                renderer.material.color = new Color(0.08f, 0.055f, 0.035f);
                return;
            }

            renderer.material.color = tile.Special == SpecialKind.None
                ? tileColors[tile.Type]
                : new Color(0.92f, 0.88f, 0.72f);
        }

        private Color GetTileDisplayColor(Tile tile)
        {
            if (tile.CrateHealth > 0)
            {
                return new Color(0.52f, 0.30f, 0.14f);
            }

            return tile.Special == SpecialKind.None
                ? tileColors[tile.Type]
                : new Color(0.92f, 0.88f, 0.72f);
        }

        private List<TileMove> ApplyGravity()
        {
            var moves = new List<TileMove>();
            for (var x = 0; x < Width; x++)
            {
                var writeY = 0;
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y] == null)
                    {
                        continue;
                    }

                    if (board[x, y].CrateHealth > 0)
                    {
                        writeY = y + 1;
                        continue;
                    }

                    if (writeY != y)
                    {
                        var tile = board[x, y];
                        board[x, writeY] = board[x, y];
                        board[x, y] = null;
                        moves.Add(new TileMove(tile, GridToWorld(x, y), GridToWorld(x, writeY), Mathf.Abs(y - writeY)));
                    }

                    writeY++;
                }
            }

            return moves;
        }

        private List<TileMove> RefillBoard()
        {
            var moves = new List<TileMove>();
            for (var x = 0; x < Width; x++)
            {
                var spawnIndex = 0;
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y] == null)
                    {
                        board[x, y] = CreateTile(x, y, RollTileTypeAvoidingMatch(x, y));
                        var target = GridToWorld(x, y);
                        var start = GridToWorld(x, Height + spawnIndex);
                        board[x, y].Object.transform.position = start;
                        moves.Add(new TileMove(board[x, y], start, target, Height + spawnIndex - y));
                        spawnIndex++;
                    }
                }
            }

            return moves;
        }

        private IEnumerator AnimateFalls(List<TileMove> moves)
        {
            if (moves.Count == 0)
            {
                yield break;
            }

            var duration = Mathf.Min(MaxFallAnimSeconds, Mathf.Max(0.12f, moves.Max(move => move.Distance) * FallAnimSecondsPerCell));
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                var t = EaseOutCubic(Mathf.Clamp01(elapsed / duration));
                foreach (var move in moves)
                {
                    if (move.Tile.Object != null)
                    {
                        move.Tile.Object.transform.position = Vector3.LerpUnclamped(move.From, move.To, t);
                    }
                }

                yield return null;
            }

            foreach (var move in moves)
            {
                if (move.Tile.Object != null)
                {
                    move.Tile.Object.transform.position = move.To;
                    move.Tile.Object.transform.localScale = Vector3.one * tileScale;
                }
            }
        }

        private float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private float EaseInBack(float t)
        {
            return Mathf.Clamp01(t * t * (2.7f * t - 1.7f));
        }

        private void CompleteRoom()
        {
            inputLocked = true;
            if (room >= RoomsPerRun)
            {
                ShowRunSummary(true, RoomsPerRun);
                return;
            }

            pendingRewardAfterRoom = room;
            room++;
            ShowUpgradeChoices();
        }

        private void ShowUpgradeChoices()
        {
            upgradeText.text = $"第 {pendingRewardAfterRoom} 关完成\n选择一个技能进入第 {room} 关";

            SetUpgradePanel(true);
            var choices = RollUpgradeChoices(pendingRewardAfterRoom);
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                if (i >= choices.Length)
                {
                    upgradeButtons[i].gameObject.SetActive(false);
                    continue;
                }

                var upgrade = choices[i];
                var button = upgradeButtons[i];
                button.gameObject.SetActive(true);
                button.GetComponent<Image>().color = GetFactionColor(upgrade.Faction);
                button.GetComponentInChildren<Text>().text = $"{GetRarityLabel(upgrade.Rarity)} {GetFactionLabel(upgrade.Faction)} {upgrade.Name}\n{upgrade.Description}";
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    activeUpgrades.Add(upgrade);
                    StartRoom();
                });
            }
        }

        private RogueUpgrade[] RollUpgradeChoices(int completedRoom)
        {
            var pool = GetUpgradePool()
                .Where(upgrade => GetUpgradeLevel(upgrade.Kind) < upgrade.MaxLevel)
                .Where(IsUpgradeUnlocked)
                .ToList();

            if (pool.Count == 0)
            {
                pool = GetUpgradePool()
                    .Where(upgrade => upgrade.IsCore && GetUpgradeLevel(upgrade.Kind) < upgrade.MaxLevel)
                    .ToList();
            }

            var choices = new List<RogueUpgrade>();
            if (completedRoom == 1)
            {
                var coreChoices = pool
                    .Where(upgrade => upgrade.IsCore)
                    .OrderBy(_ => rng.Next())
                    .ToList();
                if (coreChoices.Count > 0)
                {
                    var forcedCore = coreChoices[0];
                    choices.Add(forcedCore);
                    pool.RemoveAll(upgrade => upgrade.Kind == forcedCore.Kind);
                }
            }

            while (choices.Count < upgradeButtons.Length && pool.Count > 0)
            {
                var picked = PickWeightedUpgrade(pool);
                choices.Add(picked);
                pool.RemoveAll(upgrade => upgrade.Kind == picked.Kind);
            }

            return choices.ToArray();
        }

        private RogueUpgrade PickWeightedUpgrade(List<RogueUpgrade> pool)
        {
            var totalWeight = pool.Sum(GetUpgradeWeight);
            var roll = rng.NextDouble() * totalWeight;
            foreach (var upgrade in pool)
            {
                roll -= GetUpgradeWeight(upgrade);
                if (roll <= 0)
                {
                    return upgrade;
                }
            }

            return pool[pool.Count - 1];
        }

        private float GetUpgradeWeight(RogueUpgrade upgrade)
        {
            if (upgrade.Faction == UpgradeFaction.General)
            {
                return pendingRewardAfterRoom <= 1 ? 12f : 18f;
            }

            if (upgrade.Faction == UpgradeFaction.Bridge)
            {
                if (!HasAnyCore())
                {
                    return 0f;
                }

                var bridgeRoomBonus = pendingRewardAfterRoom >= 3 ? 16f : 6f;
                return bridgeRoomBonus + GetActiveBuildFactionCount() * 5f;
            }

            if (upgrade.IsCore)
            {
                return HasAnyCore() ? 22f : 95f;
            }

            var factionLevel = GetFactionInvestment(upgrade.Faction);
            var roomBonus = pendingRewardAfterRoom >= 2 ? 18f : 0f;
            return 62f + factionLevel * 28f + roomBonus;
        }

        private bool IsUpgradeUnlocked(RogueUpgrade upgrade)
        {
            if (upgrade.Kind == UpgradeKind.BridgeRainbowBomb)
            {
                return HasUpgrade(UpgradeKind.RainbowCore) && HasUpgrade(UpgradeKind.ExplosionCore);
            }

            if (upgrade.Kind == UpgradeKind.BridgeRainbowRocket)
            {
                return HasUpgrade(UpgradeKind.RainbowCore) && HasUpgrade(UpgradeKind.RocketCore);
            }

            return upgrade.Faction == UpgradeFaction.General ||
                   upgrade.Faction == UpgradeFaction.Bridge && HasAnyCore() ||
                   upgrade.IsCore ||
                   HasFactionCore(upgrade.Faction);
        }

        private bool HasAnyCore()
        {
            return activeUpgrades.Any(upgrade => upgrade.IsCore);
        }

        private bool HasFactionCore(UpgradeFaction faction)
        {
            return activeUpgrades.Any(upgrade => upgrade.Faction == faction && upgrade.IsCore);
        }

        private int GetFactionInvestment(UpgradeFaction faction)
        {
            return activeUpgrades.Count(upgrade => upgrade.Faction == faction);
        }

        private int GetActiveBuildFactionCount()
        {
            return activeUpgrades
                .Where(upgrade => IsBuildFaction(upgrade.Faction))
                .Select(upgrade => upgrade.Faction)
                .Distinct()
                .Count();
        }

        private int GetUpgradeLevel(UpgradeKind kind)
        {
            return activeUpgrades.Count(upgrade => upgrade.Kind == kind);
        }

        private bool HasUpgrade(UpgradeKind kind)
        {
            return GetUpgradeLevel(kind) > 0;
        }

        private RogueUpgrade[] GetUpgradePool()
        {
            return new[]
            {
                new RogueUpgrade(UpgradeKind.ExplosionCore, UpgradeFaction.Explosion, UpgradeRarity.Common, "爆破核心", "单个炸弹从3x3升级为5x5，并解锁爆破树。", 1, true),
                new RogueUpgrade(UpgradeKind.BombRadius, UpgradeFaction.Explosion, UpgradeRarity.Common, "炸弹扩容", "炸弹爆炸范围提升。", 3, false),
                new RogueUpgrade(UpgradeKind.BombSpawn, UpgradeFaction.Explosion, UpgradeRarity.Common, "越炸越多", "累计消除18/15/12个目标后生成炸弹。", 3, false),
                new RogueUpgrade(UpgradeKind.ExplosionAftershock, UpgradeFaction.Explosion, UpgradeRarity.Rare, "爆炸余波", "炸弹额外清除周围棋子。", 2, false),

                new RogueUpgrade(UpgradeKind.RocketCore, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭核心", "单个火箭从短程升级为整行/整列，并解锁火箭树。", 1, true),
                new RogueUpgrade(UpgradeKind.RocketSplit, UpgradeFaction.Rocket, UpgradeRarity.Rare, "火箭分裂", "火箭额外扫过交叉方向。", 2, false),
                new RogueUpgrade(UpgradeKind.RocketExtra, UpgradeFaction.Rocket, UpgradeRarity.Common, "额外发射", "每第2个火箭额外扫相邻轨道。", 3, false),
                new RogueUpgrade(UpgradeKind.RocketSpawn, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭补给", "累计消除16/13/10个目标后生成火箭。", 3, false),

                new RogueUpgrade(UpgradeKind.RainbowCore, UpgradeFaction.Rainbow, UpgradeRarity.Rare, "彩虹核心", "彩球清除目标颜色100%，每关开局生成1个。", 1, true),
                new RogueUpgrade(UpgradeKind.RainbowCopy, UpgradeFaction.Rainbow, UpgradeRarity.Epic, "彩虹复制", "每局前2次彩球触发后必定复制。", 2, false),
                new RogueUpgrade(UpgradeKind.RainbowSpawn, UpgradeFaction.Rainbow, UpgradeRarity.Rare, "彩虹凝结", "累计消除28/24/20个目标后生成彩球。", 3, false),
                new RogueUpgrade(UpgradeKind.RainbowChainBomb, UpgradeFaction.Rainbow, UpgradeRarity.Rare, "彩爆连锁", "彩球触发时额外制造爆点。", 2, false),

                new RogueUpgrade(UpgradeKind.PropellerCore, UpgradeFaction.Propeller, UpgradeRarity.Common, "螺旋桨核心", "单个螺旋桨额外锁定1个关键目标，并解锁螺旋桨树。", 1, true),
                new RogueUpgrade(UpgradeKind.PropellerSpawn, UpgradeFaction.Propeller, UpgradeRarity.Common, "起飞补给", "累计消除14/12/10个目标后生成螺旋桨。", 3, false),
                new RogueUpgrade(UpgradeKind.PropellerBlast, UpgradeFaction.Propeller, UpgradeRarity.Rare, "精准轰炸", "螺旋桨目标范围扩大。", 2, false),
                new RogueUpgrade(UpgradeKind.PropellerSwarm, UpgradeFaction.Propeller, UpgradeRarity.Rare, "机群出动", "每第3个螺旋桨追加飞行一次。", 2, false),

                new RogueUpgrade(UpgradeKind.BridgeRocketBomb, UpgradeFaction.Bridge, UpgradeRarity.Epic, "火药推进", "火箭与爆炸流更容易互相点燃。", 1, false),
                new RogueUpgrade(UpgradeKind.BridgeRainbowBomb, UpgradeFaction.Bridge, UpgradeRarity.Epic, "彩虹燃爆", "彩球触发后优先引爆炸弹，否则生成炸弹。", 1, false),
                new RogueUpgrade(UpgradeKind.BridgeRainbowRocket, UpgradeFaction.Bridge, UpgradeRarity.Epic, "彩虹制导", "彩球触发后生成火箭，并扫目标最多的行列。", 1, false)
            };
        }

        private void SetUpgradePanel(bool visible)
        {
            upgradeText.gameObject.SetActive(visible);
            upgradePanelOpen = visible;
            foreach (var button in upgradeButtons)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private void FailRun()
        {
            ShowRunSummary(false, room);
        }

        private void ShowRunSummary(bool success, int reachedRoom)
        {
            inputLocked = true;
            SetUpgradePanel(false);
            upgradeText.gameObject.SetActive(true);
            restartButton.gameObject.SetActive(true);
            restartButton.GetComponentInChildren<Text>().text = "再来一局";
            endlessButton.gameObject.SetActive(false);

            var completedRooms = success ? RoomsPerRun : Mathf.Max(0, reachedRoom - 1);
            upgradeText.text =
                $"{(success ? "Run 通关！" : "Run 失败")}\n" +
                $"进度：第 {reachedRoom}/{RoomsPerRun} 关（已完成 {completedRooms} 关）\n" +
                $"主要流派：{GetMainBuildName()}\n" +
                $"本局技能：{FormatActiveUpgrades()}\n" +
                $"总分 {runScore} / 最高连锁 {Mathf.Max(1, bestComboChain)}";
        }

        private void RefreshStatus()
        {
            targetScore = GetAdjustedTargetScore();
            statusText.text =
                $"短Run  第 {room}/{RoomsPerRun} 关\n" +
                $"目标：清除木箱  剩余 {remainingCrates}/{totalCrates}\n" +
                $"分数 {score}\n" +
                $"剩余步数 {movesRemaining} / {roomMoveLimit}\n" +
                $"Run总分 {runScore} / 最高连锁 {Mathf.Max(1, bestComboChain)}\n" +
                $"已选强化：{FormatActiveUpgrades()}\n" +
                GetRoomGoalText();
        }

        private string FormatActiveUpgrades()
        {
            if (activeUpgrades.Count == 0)
            {
                return "无";
            }

            return string.Join("\n", activeUpgrades
                .GroupBy(upgrade => upgrade.Faction)
                .Select(factionGroup => $"{GetFactionLabel(factionGroup.Key)} " +
                    string.Join("、", factionGroup
                        .GroupBy(upgrade => upgrade.Name)
                        .Select(group => group.Count() > 1 ? $"{group.Key} Lv.{group.Count()}" : group.Key))));
        }

        private string GetRoomGoalText()
        {
            switch (room)
            {
                case 1:
                    return "第1关：清掉少量木箱，通关后必出核心技能。";
                case 2:
                    return "第2关：木箱变多，观察核心技能清目标。";
                case 3:
                    return "第3关：利用Build处理更多双层木箱。";
                default:
                    return "第4关：清空最多木箱，让Build表演起来。";
            }
        }

        private string GetMainBuildName()
        {
            var factionCounts = activeUpgrades
                .Where(upgrade => IsBuildFaction(upgrade.Faction))
                .GroupBy(upgrade => upgrade.Faction)
                .Select(group => new { Faction = group.Key, Count = group.Count() })
                .OrderByDescending(group => group.Count)
                .ToList();

            if (factionCounts.Count == 0)
            {
                return activeUpgrades.Any(upgrade => upgrade.Faction == UpgradeFaction.Propeller) ? "螺旋桨辅助" : "未成型";
            }

            if (factionCounts.Count > 1 && factionCounts[0].Count == factionCounts[1].Count)
            {
                return "混合流";
            }

            return GetFactionBuildName(factionCounts[0].Faction);
        }

        private bool IsBuildFaction(UpgradeFaction faction)
        {
            return faction == UpgradeFaction.Explosion ||
                   faction == UpgradeFaction.Rocket ||
                   faction == UpgradeFaction.Rainbow;
        }

        private string GetFactionBuildName(UpgradeFaction faction)
        {
            switch (faction)
            {
                case UpgradeFaction.Explosion:
                    return "爆炸流";
                case UpgradeFaction.Rocket:
                    return "火箭流";
                case UpgradeFaction.Rainbow:
                    return "彩虹流";
                case UpgradeFaction.Propeller:
                    return "螺旋桨辅助";
                default:
                    return "混合流";
            }
        }

        private bool IsPointerOverUi()
        {
            return upgradePanelOpen || EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private int GetAdjustedTargetScore()
        {
            return Mathf.Max(300, baseTargetScore);
        }

        private Color GetFactionColor(UpgradeFaction faction)
        {
            switch (faction)
            {
                case UpgradeFaction.Explosion:
                    return new Color(0.62f, 0.20f, 0.12f, 0.96f);
                case UpgradeFaction.Rocket:
                    return new Color(0.10f, 0.34f, 0.54f, 0.96f);
                case UpgradeFaction.Rainbow:
                    return new Color(0.44f, 0.18f, 0.58f, 0.96f);
                case UpgradeFaction.Propeller:
                    return new Color(0.15f, 0.48f, 0.32f, 0.96f);
                case UpgradeFaction.Bridge:
                    return new Color(0.54f, 0.42f, 0.12f, 0.96f);
                case UpgradeFaction.General:
                    return new Color(0.28f, 0.27f, 0.32f, 0.96f);
                default:
                    return new Color(0.22f, 0.20f, 0.29f, 0.95f);
            }
        }

        private string GetFactionLabel(UpgradeFaction faction)
        {
            switch (faction)
            {
                case UpgradeFaction.Explosion:
                    return "[爆炸]";
                case UpgradeFaction.Rocket:
                    return "[火箭]";
                case UpgradeFaction.Rainbow:
                    return "[彩虹]";
                case UpgradeFaction.Propeller:
                    return "[辅助]";
                case UpgradeFaction.Bridge:
                    return "[桥接]";
                case UpgradeFaction.General:
                    return "[通用]";
                default:
                    return "[通用]";
            }
        }

        private string GetRarityLabel(UpgradeRarity rarity)
        {
            switch (rarity)
            {
                case UpgradeRarity.Rare:
                    return "◆";
                case UpgradeRarity.Epic:
                    return "★";
                default:
                    return "•";
            }
        }

        private RogueUpgrade GetUpgradeDefinition(UpgradeKind kind)
        {
            return GetUpgradePool().FirstOrDefault(upgrade => upgrade.Kind == kind);
        }

        private void ShowUpgradeTrigger(RogueUpgrade upgrade)
        {
            if (string.IsNullOrEmpty(upgrade.Name))
            {
                return;
            }

            ShowTriggerText($"{upgrade.Name}触发！", GetFactionColor(upgrade.Faction));
        }

        private IEnumerator ShowTriggerTextRoutine(string message, Color color)
        {
            triggerText.text = message;
            triggerText.color = new Color(color.r + 0.25f, color.g + 0.25f, color.b + 0.25f, 1f);
            triggerText.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.5f);
            triggerText.gameObject.SetActive(false);
            triggerTextRoutine = null;
        }

        private Font GetRuntimeFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private bool TryGetPrimaryPressPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            screenPosition = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }

            screenPosition = Vector2.zero;
            return false;
#endif
        }

        private Vector3 GridToWorld(int x, int y)
        {
            return boardOrigin + new Vector3(x * tileSpacing, y * tileSpacing, 0f);
        }

        private Vector2Int WorldToGrid(Vector3 world)
        {
            var local = world - boardOrigin;
            return new Vector2Int(Mathf.RoundToInt(local.x / tileSpacing), Mathf.RoundToInt(local.y / tileSpacing));
        }

        private void RefreshBoardTransforms()
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var tile = board[x, y];
                    if (tile == null)
                    {
                        continue;
                    }

                    tile.Object.transform.position = GridToWorld(x, y);
                    tile.Object.transform.localScale = Vector3.one * tileScale;
                }
            }

            if (boardRoot == null)
            {
                return;
            }

            foreach (Transform child in boardRoot)
            {
                if (!child.name.StartsWith("Cell ", StringComparison.Ordinal))
                {
                    continue;
                }

                var coordinateText = child.name.Substring(5);
                var parts = coordinateText.Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], out var x) ||
                    !int.TryParse(parts[1], out var y))
                {
                    continue;
                }

                child.position = GridToWorld(x, y) + new Vector3(0f, 0f, 0.2f);
                child.localScale = Vector3.one * (tileScale + 0.08f);
            }
        }

        private bool IsInside(Vector2Int pos)
        {
            return pos.x >= 0 && pos.x < Width && pos.y >= 0 && pos.y < Height;
        }

        private bool IsSpecialTile(Vector2Int pos)
        {
            return IsInside(pos) && board[pos.x, pos.y] != null && board[pos.x, pos.y].Special != SpecialKind.None;
        }

        private bool HasSameMatchColor(Tile first, Tile second)
        {
            return IsColorTile(first) && IsColorTile(second) && first.Type == second.Type;
        }

        private bool IsColorTile(Tile tile)
        {
            return tile != null && tile.Special == SpecialKind.None && tile.CrateHealth <= 0;
        }

        private bool IsRocket(SpecialKind special)
        {
            return special == SpecialKind.LineHorizontal || special == SpecialKind.LineVertical;
        }

        private sealed class Tile
        {
            public Tile(int type, SpecialKind special, GameObject tileObject)
            {
                Type = type;
                Special = special;
                Object = tileObject;
            }

            public int Type { get; }
            public SpecialKind Special { get; set; }
            public int CrateHealth { get; set; }
            public GameObject Object { get; }
        }

        private sealed class LevelConfig
        {
            public LevelConfig(int levelIndex, int boardWidth, int boardHeight, int moveLimit, string[] boxMapRows)
            {
                LevelIndex = levelIndex;
                BoardWidth = boardWidth;
                BoardHeight = boardHeight;
                MoveLimit = moveLimit;
                BoxMapRows = boxMapRows ?? Array.Empty<string>();
            }

            public int LevelIndex { get; }
            public int BoardWidth { get; }
            public int BoardHeight { get; }
            public int MoveLimit { get; }
            public string[] BoxMapRows { get; }
        }

        private sealed class MatchGroup
        {
            public MatchGroup(List<Vector2Int> positions, MatchOrientation orientation)
            {
                Positions = positions;
                Orientation = orientation;
            }

            public List<Vector2Int> Positions { get; }
            public MatchOrientation Orientation { get; }
        }

        private struct PendingSpecial
        {
            public PendingSpecial(Vector2Int position, SpecialKind special)
            {
                Position = position;
                Special = special;
            }

            public Vector2Int Position { get; }
            public SpecialKind Special { get; }
        }

        private struct TileMove
        {
            public TileMove(Tile tile, Vector3 from, Vector3 to, int distance)
            {
                Tile = tile;
                From = from;
                To = to;
                Distance = distance;
            }

            public Tile Tile { get; }
            public Vector3 From { get; }
            public Vector3 To { get; }
            public int Distance { get; }
        }

        private struct HintMove
        {
            public HintMove(Vector2Int from, Vector2Int to, float score)
            {
                From = from;
                To = to;
                Score = score;
            }

            public Vector2Int From { get; }
            public Vector2Int To { get; }
            public float Score { get; }
        }

        private struct RogueUpgrade
        {
            public RogueUpgrade(UpgradeKind kind, UpgradeFaction faction, UpgradeRarity rarity, string name, string description, int maxLevel, bool isCore)
            {
                Kind = kind;
                Faction = faction;
                Rarity = rarity;
                Name = name;
                Description = description;
                MaxLevel = maxLevel;
                IsCore = isCore;
            }

            public UpgradeKind Kind { get; }
            public UpgradeFaction Faction { get; }
            public UpgradeRarity Rarity { get; }
            public string Name { get; }
            public string Description { get; }
            public int MaxLevel { get; }
            public bool IsCore { get; }
        }

        private enum UpgradeRarity
        {
            Common,
            Rare,
            Epic
        }

        private enum UpgradeKind
        {
            ExplosionCore,
            BombRadius,
            BombSpawn,
            ExplosionAftershock,
            RocketCore,
            RocketSplit,
            RocketExtra,
            RocketSpawn,
            RainbowCore,
            RainbowCopy,
            RainbowSpawn,
            RainbowChainBomb,
            PropellerCore,
            PropellerSpawn,
            PropellerBlast,
            PropellerSwarm,
            BridgeRocketBomb,
            BridgeRainbowBomb,
            BridgeRainbowRocket
        }

        private enum UpgradeFaction
        {
            Explosion,
            Rocket,
            Rainbow,
            Propeller,
            Bridge,
            General
        }

        private enum MatchOrientation
        {
            Horizontal,
            Vertical,
            Square
        }

        private enum CrateLayoutTemplate
        {
            OpenTopBottomTargets,
            OpenCenterEdgeTargets,
            SideTargetsMiddleLane,
            Channel,
            DenseCluster,
            Mixed
        }

        private enum PropellerTargetMode
        {
            Single,
            BombCarrier,
            RocketCarrierHorizontal,
            RocketCarrierVertical,
            Multi
        }

        private enum SpecialKind
        {
            None,
            LineHorizontal,
            LineVertical,
            Bomb,
            Rainbow,
            Propeller
        }
    }
}
