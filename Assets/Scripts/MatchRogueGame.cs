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
        private const int RoomsPerRun = 6;
        private const string LeaderboardPrefsKey = "MatchRogue.RunLeaderboard.v1";
        private const string LevelConfigResourcePattern = "Configs/Levels/Level_{0:00}";

        private static readonly int[] RoomMoveLimits = { 32, 32, 34, 34, 36, 42 };
        private static readonly int[] RoomTargetScores = { 14000, 22000, 34000, 50000, 70000, 95000 };
        private static readonly int[] RoomCrateCounts = { 14, 18, 24, 29, 34, 41 };
        private static readonly int[] RoomTwoLayerCrateCounts = { 3, 4, 9, 14, 21, 25 };
        private static readonly int[] RoomThreeLayerCrateCounts = { 0, 0, 2, 4, 5, 8 };
        private static readonly LevelConfig[] LevelConfigs =
        {
            new LevelConfig(
                1,
                9,
                11,
                32,
                new[]
                {
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    ".........",
                    "..11111..",
                    ".1122211.",
                    ".........",
                    "...1.1...",
                    "........."
                }),
            new LevelConfig(
                2,
                9,
                11,
                32,
                new[]
                {
                    ".........",
                    ".........",
                    ".........",
                    "..11111..",
                    ".1122211.",
                    ".........",
                    "....2....",
                    ".111.....",
                    ".........",
                    ".1.....1.",
                    "........."
                }),
            new LevelConfig(
                3,
                9,
                11,
                34,
                new[]
                {
                    ".........",
                    ".........",
                    ".........",
                    "..11111..",
                    ".1222221.",
                    "..2332...",
                    ".........",
                    "11.....11",
                    "...22....",
                    ".........",
                    ".1.....1."
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
                    "..11111..",
                    ".1222221.",
                    ".1233321.",
                    ".........",
                    "1...3...1",
                    ".22...22.",
                    "...222...",
                    ".........",
                    "........."
                }),
            new LevelConfig(
                5,
                9,
                11,
                36,
                new[]
                {
                    ".........",
                    ".111.111.",
                    ".222.222.",
                    ".23...32.",
                    ".22...22.",
                    ".........",
                    "22.....22",
                    "13.....31",
                    ".2..3..2.",
                    "...222...",
                    "........."
                }),
            new LevelConfig(
                6,
                9,
                11,
                42,
                new[]
                {
                    "11.....11",
                    "223...322",
                    "23.....32",
                    "22.....22",
                    ".........",
                    ".........",
                    "22.....22",
                    "23.....32",
                    "223...322",
                    "11.....11",
                    "..22222.."
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
        private int[,] iceHealth;
        private GameObject[,] iceOverlays;
        private readonly List<RogueUpgrade> activeUpgrades = new List<RogueUpgrade>();
        private readonly HashSet<int> removedTileTypes = new HashSet<int>();
        private readonly Dictionary<Vector2Int, int> crateDamageBonuses = new Dictionary<Vector2Int, int>();
        private readonly Dictionary<UpgradeFaction, int> missedCoreFactionChoiceCounts = new Dictionary<UpgradeFaction, int>();
        private readonly List<PendingSpecial> pendingPostClearSpecials = new List<PendingSpecial>();
        private readonly System.Random rng = new System.Random();

        private Camera mainCamera;
        private Transform boardRoot;
        private Transform backgroundQuad;
        private Canvas canvas;
        private Text statusText;
        private Text upgradeText;
        private Text triggerText;
        private Image summaryPanel;
        private RectTransform statusRect;
        private Button[] upgradeButtons;
        private Button[] upgradeRefreshButtons;
        private Button extraSkillAdButton;
        private Button extraMovesAdButton;
        private Button shuffleAdButton;
        private Button restartButton;
        private Button endlessButton;
        private Texture2D lineHorizontalIcon;
        private Texture2D lineVerticalIcon;
        private Texture2D bombIcon;
        private Texture2D rainbowIcon;
        private Texture2D propellerIcon;
        private Texture2D backgroundTexture;
        private Texture2D crateTexture;
        private Texture2D iceTexture;
        private Coroutine triggerTextRoutine;

        private Vector2Int? selected;
        private bool inputLocked;
        private bool upgradePanelOpen;
        private bool choiceStartsRoomAfterSelection;
        private bool extraSkillAdUsedThisRun;
        private bool extraMovesAdUsedThisLevel;
        private bool shuffleAdUsedThisLevel;
        private readonly bool[] optionRefreshUsed = new bool[3];
        private readonly List<RogueUpgrade> currentUpgradeChoices = new List<RogueUpgrade>();
        private readonly HashSet<UpgradeKind> displayedUpgradeKindsThisChoice = new HashSet<UpgradeKind>();
        private int room = 1;
        private int pendingRewardAfterRoom;
        private int currentUpgradeChoiceCompletedRoom;
        private int score;
        private int runScore;
        private int baseTargetScore;
        private int targetScore;
        private int totalCrates;
        private int remainingCrates;
        private int totalIce;
        private int remainingIce;
        private int movesRemaining;
        private int roomMoveLimit;
        private int currentColorCount = TileTypes;
        private int comboChain;
        private int bestComboChain;
        private int rocketActivationCount;
        private int bombActivationCount;
        private int rainbowActivationCount;
        private int propellerActivationCount;
        private int clearedBoxCount;
        private int boxDamageTotal;
        private int clearedIceCount;
        private int iceDamageTotal;
        private int maxSingleClearCount;
        private int totalMovesUsed;
        private int extraMovesAdUseCount;
        private int shuffleAdUseCount;
        private int skillRefreshAdUseCount;
        private int currentRunLeaderboardId;
        private DateTime runStartTime;
        private readonly List<int> levelRemainingMoves = new List<int>();
        private int bombSpawnClearProgress;
        private int rocketSpawnClearProgress;
        private int rainbowSpawnClearProgress;
        private int propellerSpawnClearProgress;
        private int edgeWalkerClearProgress;
        private int bottomSweepClearProgress;
        private int bombCoreClearProgress;
        private int rocketCoreClearProgress;
        private int rainbowCoreClearProgress;
        private int propellerRebirthMatchProgress;
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
            iceHealth = new int[Width, Height];
            iceOverlays = new GameObject[Width, Height];
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
            iceTexture = Resources.Load<Texture2D>("Targets/IceBlock");
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
            summaryPanel = CreatePanel("Run Summary Panel", new Vector2(0f, -540f), new Vector2(1040f, 1180f), new Color(0.06f, 0.07f, 0.10f, 0.94f));
            summaryPanel.gameObject.SetActive(false);

            upgradeText = CreateText("UpgradeTitle", new Vector2(0f, -430f), new Vector2(1000f, 120f), 40, TextAnchor.MiddleCenter);
            upgradeText.text = "";
            triggerText = CreateText("TriggerText", new Vector2(0f, -315f), new Vector2(760f, 80f), 34, TextAnchor.MiddleCenter);
            triggerText.text = "";
            triggerText.gameObject.SetActive(false);

            upgradeButtons = new Button[3];
            upgradeRefreshButtons = new Button[3];
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                upgradeButtons[i] = CreateButton($"Upgrade {i + 1}", new Vector2(0f, 250f - i * 230f), new Vector2(980f, 158f));
                upgradeButtons[i].GetComponentInChildren<Text>().fontSize = 34;
                upgradeRefreshButtons[i] = CreateButton($"Upgrade Refresh {i + 1}", new Vector2(0f, 145f - i * 230f), new Vector2(360f, 56f));
                upgradeRefreshButtons[i].GetComponentInChildren<Text>().fontSize = 26;
                upgradeRefreshButtons[i].GetComponentInChildren<Text>().text = "广告刷新";
            }

            extraSkillAdButton = CreateButton("Ad Extra Skill", new Vector2(-330f, -870f), new Vector2(300f, 70f));
            extraSkillAdButton.GetComponentInChildren<Text>().fontSize = 24;
            extraSkillAdButton.GetComponentInChildren<Text>().text = "广告选技能";
            extraSkillAdButton.onClick.AddListener(OnExtraSkillAdClicked);

            extraMovesAdButton = CreateButton("Ad Extra Moves", new Vector2(0f, -870f), new Vector2(260f, 70f));
            extraMovesAdButton.GetComponentInChildren<Text>().fontSize = 24;
            extraMovesAdButton.GetComponentInChildren<Text>().text = "广告+5步";
            extraMovesAdButton.onClick.AddListener(OnExtraMovesAdClicked);

            shuffleAdButton = CreateButton("Ad Shuffle", new Vector2(310f, -870f), new Vector2(260f, 70f));
            shuffleAdButton.GetComponentInChildren<Text>().fontSize = 24;
            shuffleAdButton.GetComponentInChildren<Text>().text = "广告洗牌";
            shuffleAdButton.onClick.AddListener(OnShuffleAdClicked);

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

        private Image CreatePanel(string name, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
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
            removedTileTypes.Clear();
            crateDamageBonuses.Clear();
            missedCoreFactionChoiceCounts.Clear();
            pendingPostClearSpecials.Clear();
            extraSkillAdUsedThisRun = false;
            extraMovesAdUsedThisLevel = false;
            shuffleAdUsedThisLevel = false;
            currentUpgradeChoices.Clear();
            selected = null;
            inputLocked = false;
            score = 0;
            runScore = 0;
            bestComboChain = 0;
            rocketActivationCount = 0;
            bombActivationCount = 0;
            rainbowActivationCount = 0;
            propellerActivationCount = 0;
            clearedBoxCount = 0;
            boxDamageTotal = 0;
            clearedIceCount = 0;
            iceDamageTotal = 0;
            maxSingleClearCount = 0;
            totalMovesUsed = 0;
            extraMovesAdUseCount = 0;
            shuffleAdUseCount = 0;
            skillRefreshAdUseCount = 0;
            currentRunLeaderboardId = 0;
            runStartTime = DateTime.Now;
            levelRemainingMoves.Clear();
            bombSpawnClearProgress = 0;
            rocketSpawnClearProgress = 0;
            rainbowSpawnClearProgress = 0;
            propellerSpawnClearProgress = 0;
            edgeWalkerClearProgress = 0;
            bottomSweepClearProgress = 0;
            bombCoreClearProgress = 0;
            rocketCoreClearProgress = 0;
            rainbowCoreClearProgress = 0;
            propellerRebirthMatchProgress = 0;
            MarkEffectiveAction();
            restartButton.GetComponentInChildren<Text>().text = "重新开始";
            restartButton.gameObject.SetActive(true);
            endlessButton.gameObject.SetActive(false);
            summaryPanel.gameObject.SetActive(false);
            RefreshAdButtons();
            ShowUpgradeChoices(0, true);
        }

        private void StartRoom()
        {
            inputLocked = false;
            extraMovesAdUsedThisLevel = false;
            shuffleAdUsedThisLevel = false;
            selected = null;
            var roomIndex = Mathf.Clamp(room - 1, 0, RoomsPerRun - 1);
            var levelConfig = GetLevelConfig(room);
            roomMoveLimit = levelConfig?.MoveLimit ?? RoomMoveLimits[roomIndex];
            currentColorCount = Mathf.Clamp(levelConfig?.ColorCount ?? TileTypes, 3, TileTypes);
            GenerateBoard();
            PlaceRoomTargets(levelConfig);
            SeedShowcaseSpecialsForRoom();
            EnsurePlayableBoard();
            RefreshBoardTransforms();
            score = 0;
            comboChain = 0;
            movesRemaining = roomMoveLimit;
            baseTargetScore = RoomTargetScores[roomIndex];
            targetScore = GetAdjustedTargetScore();
            SetUpgradePanel(false);
            MarkEffectiveAction();
            RefreshStatus();
            RefreshAdButtons();
        }

        private void GenerateBoard()
        {
            ClearTiles();
            totalCrates = 0;
            remainingCrates = 0;
            totalIce = 0;
            remainingIce = 0;
            Array.Clear(iceHealth, 0, iceHealth.Length);

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var type = RollTileTypeAvoidingMatch(x, y);
                    board[x, y] = CreateTile(x, y, type);
                }
            }
        }

        private void PlaceRoomTargets(LevelConfig levelConfig)
        {
            var roomIndex = Mathf.Clamp(room - 1, 0, RoomsPerRun - 1);
            if (levelConfig != null && TryPlaceConfiguredTargets(levelConfig))
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
            return LoadCsvLevelConfig(levelIndex) ?? LevelConfigs.FirstOrDefault(level => level.LevelIndex == levelIndex);
        }

        private bool TryPlaceConfiguredTargets(LevelConfig config)
        {
            if (config.BoardWidth != Width || config.BoardHeight != Height)
            {
                Debug.LogWarning($"Level {config.LevelIndex} board size is {config.BoardWidth}x{config.BoardHeight}, current board is {Width}x{Height}. Out-of-range cells will be skipped.");
            }

            totalCrates = 0;
            remainingCrates = 0;
            totalIce = 0;
            remainingIce = 0;
            Array.Clear(iceHealth, 0, iceHealth.Length);

            for (var row = 0; row < config.GridRows.Length; row++)
            {
                var tokens = NormalizeGridTokens(SplitCsvLine(config.GridRows[row] ?? string.Empty), config.BoardWidth);
                if (tokens.Length != config.BoardWidth)
                {
                    Debug.LogWarning($"Level {config.LevelIndex} row {row} has width {tokens.Length}, expected {config.BoardWidth}.");
                }

                // CSV grid rows are authored top to bottom: first row is the top of the board,
                // last row is the bottom. Runtime grid coordinates use a bottom-left origin.
                var y = Height - 1 - row;
                for (var x = 0; x < Mathf.Min(tokens.Length, config.BoardWidth); x++)
                {
                    var pos = new Vector2Int(x, y);
                    if (!IsInside(pos))
                    {
                        Debug.LogWarning($"Level {config.LevelIndex} cell ({x},{y}) is outside the {Width}x{Height} board and was skipped.");
                        continue;
                    }

                    var cell = ParseLevelCellToken(config.LevelIndex, row, x, tokens[x]);
                    ApplyLevelCell(pos, cell);
                }
            }

            remainingCrates = totalCrates;
            remainingIce = totalIce;
            RefreshAllIceOverlays();
            if (totalCrates <= 0 && totalIce <= 0)
            {
                Debug.LogWarning($"Level {config.LevelIndex} has no valid targets. Falling back to template crate placement.");
                return false;
            }

            return true;
        }

        private LevelConfig LoadCsvLevelConfig(int levelIndex)
        {
            var assetPath = string.Format(LevelConfigResourcePattern, levelIndex);
            var textAsset = Resources.Load<TextAsset>(assetPath);
            if (textAsset == null)
            {
                Debug.LogError($"Missing level CSV Resources/{assetPath}.csv. Falling back to built-in level {levelIndex}.");
                return null;
            }

            try
            {
                return ParseLevelCsv(levelIndex, textAsset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to parse level CSV {assetPath}: {exception.Message}. Falling back to built-in level {levelIndex}.");
                return null;
            }
        }

        private LevelConfig ParseLevelCsv(int levelIndex, string csv)
        {
            var lines = csv
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var gridRows = new List<string>();
            var readingGrid = false;
            foreach (var line in lines)
            {
                var parts = SplitCsvLine(line);
                if (parts.Length > 0 && string.Equals(parts[0], "grid", StringComparison.OrdinalIgnoreCase))
                {
                    readingGrid = true;
                    continue;
                }

                if (readingGrid)
                {
                    gridRows.Add(line);
                    continue;
                }

                if (parts.Length >= 2)
                {
                    values[parts[0].Trim()] = parts[1].Trim();
                }
            }

            var boardWidthValue = ParseRequiredLevelInt(values, "boardWidth", Width, levelIndex);
            var boardHeightValue = ParseRequiredLevelInt(values, "boardHeight", Height, levelIndex);
            var moveLimitValue = ParseRequiredLevelInt(values, "moveLimit", RoomMoveLimits[Mathf.Clamp(levelIndex - 1, 0, RoomMoveLimits.Length - 1)], levelIndex);
            var colorCountValue = ParseOptionalLevelInt(values, "colorCount", TileTypes);
            var levelName = values.TryGetValue("levelName", out var parsedLevelName) ? parsedLevelName : $"Level {levelIndex:00}";

            if (gridRows.Count == 0)
            {
                Debug.LogWarning($"Level {levelIndex} CSV has no grid section.");
            }
            else if (gridRows.Count != boardHeightValue)
            {
                Debug.LogWarning($"Level {levelIndex} CSV grid has {gridRows.Count} rows, expected {boardHeightValue}. Missing rows are treated as empty.");
            }

            while (gridRows.Count < boardHeightValue)
            {
                gridRows.Add(".");
            }

            if (gridRows.Count > boardHeightValue)
            {
                gridRows.RemoveRange(boardHeightValue, gridRows.Count - boardHeightValue);
            }

            return new LevelConfig(levelIndex, levelName, boardWidthValue, boardHeightValue, moveLimitValue, Mathf.Clamp(colorCountValue, 3, TileTypes), gridRows.ToArray());
        }

        private int ParseRequiredLevelInt(Dictionary<string, string> values, string key, int fallback, int levelIndex)
        {
            if (!values.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value))
            {
                Debug.LogWarning($"Level {levelIndex} CSV is missing or has invalid {key}. Using {fallback}.");
                return fallback;
            }

            return value;
        }

        private int ParseOptionalLevelInt(Dictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) ? value : fallback;
        }

        private string[] SplitCsvLine(string line)
        {
            return line.Split(',').Select(part => part.Trim()).ToArray();
        }

        private string[] NormalizeGridTokens(string[] tokens, int expectedWidth)
        {
            return tokens.Length <= expectedWidth ? tokens : tokens.Take(expectedWidth).ToArray();
        }

        private LevelCell ParseLevelCellToken(int levelIndex, int row, int x, string rawToken)
        {
            var token = string.IsNullOrWhiteSpace(rawToken) ? "." : rawToken.Trim();
            if (token == ".")
            {
                return LevelCell.Empty;
            }

            var cursor = 0;
            var crateHp = 0;
            var iceHp = 0;
            if (cursor < token.Length && token[cursor] == 'B')
            {
                cursor++;
                if (!TryParseLayerDigit(token, ref cursor, out crateHp))
                {
                    WarnInvalidLevelToken(levelIndex, row, x, token);
                    return LevelCell.Empty;
                }
            }

            if (cursor < token.Length && token[cursor] == 'I')
            {
                cursor++;
                if (!TryParseLayerDigit(token, ref cursor, out iceHp))
                {
                    WarnInvalidLevelToken(levelIndex, row, x, token);
                    return LevelCell.Empty;
                }
            }

            if (cursor != token.Length || (crateHp <= 0 && iceHp <= 0))
            {
                WarnInvalidLevelToken(levelIndex, row, x, token);
                return LevelCell.Empty;
            }

            return new LevelCell(crateHp, iceHp);
        }

        private bool TryParseLayerDigit(string token, ref int cursor, out int value)
        {
            value = 0;
            if (cursor >= token.Length || token[cursor] < '1' || token[cursor] > '3')
            {
                return false;
            }

            value = token[cursor] - '0';
            cursor++;
            return true;
        }

        private void WarnInvalidLevelToken(int levelIndex, int row, int x, string token)
        {
            Debug.LogWarning($"Level {levelIndex} has invalid token '{token}' at grid row {row}, x {x}. Treating as empty.");
        }

        private void ApplyLevelCell(Vector2Int pos, LevelCell cell)
        {
            if (cell.CrateHealth > 0)
            {
                board[pos.x, pos.y].CrateHealth = cell.CrateHealth;
                totalCrates++;
                DecorateTile(board[pos.x, pos.y]);
            }

            if (cell.IceHealth > 0)
            {
                iceHealth[pos.x, pos.y] = cell.IceHealth;
                totalIce++;
            }
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
            foreach (var type in GetAvailableTileTypes().OrderBy(_ => rng.Next()))
            {
                if (!WouldCreateImmediateMatch(x, y, type))
                {
                    return type;
                }
            }

            var fallbackTypes = GetAvailableTileTypes().ToList();
            return fallbackTypes.Count > 0 ? fallbackTypes[rng.Next(fallbackTypes.Count)] : rng.Next(currentColorCount);
        }

        private IEnumerable<int> GetAvailableTileTypes()
        {
            return Enumerable.Range(0, currentColorCount).Where(type => !removedTileTypes.Contains(type));
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
            ClearIceOverlays();
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

        private void ClearIceOverlays()
        {
            if (iceOverlays == null)
            {
                return;
            }

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (iceOverlays[x, y] != null)
                    {
                        Destroy(iceOverlays[x, y]);
                        iceOverlays[x, y] = null;
                    }
                }
            }
        }

        private void RefreshAllIceOverlays()
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    RefreshIceOverlay(new Vector2Int(x, y));
                }
            }
        }

        private void RefreshIceOverlay(Vector2Int pos)
        {
            if (!IsInside(pos))
            {
                return;
            }

            var hp = iceHealth[pos.x, pos.y];
            if (hp <= 0)
            {
                if (iceOverlays[pos.x, pos.y] != null)
                {
                    Destroy(iceOverlays[pos.x, pos.y]);
                    iceOverlays[pos.x, pos.y] = null;
                }

                return;
            }

            var overlay = iceOverlays[pos.x, pos.y];
            if (overlay == null)
            {
                overlay = new GameObject($"Ice {pos.x},{pos.y}");
                overlay.name = $"Ice {pos.x},{pos.y}";
                overlay.transform.SetParent(boardRoot);
                iceOverlays[pos.x, pos.y] = overlay;
            }

            overlay.transform.position = GridToWorld(pos.x, pos.y);
            overlay.transform.localScale = Vector3.one;
            var hasCrateOnTop = board[pos.x, pos.y] != null && board[pos.x, pos.y].CrateHealth > 0;
            var edgeAlpha = hasCrateOnTop ? 0.92f : 0.98f;
            var fillAlpha = hasCrateOnTop ? 0.12f : 0.24f;
            var baseTint = hp == 1
                ? new Color(0.82f, 0.98f, 1f, 1f)
                : hp == 2
                    ? new Color(0.55f, 0.88f, 1f, 1f)
                    : new Color(0.30f, 0.66f, 1f, 1f);

            var full = tileScale + 0.16f;
            var edge = Mathf.Max(0.08f, tileScale * 0.16f);
            ConfigureIcePart(overlay.transform, "IceFill", new Vector3(0f, 0f, -0.105f), new Vector3(full, full, 1f), WithAlpha(baseTint, fillAlpha));
            ConfigureIcePart(overlay.transform, "IceTop", new Vector3(0f, full * 0.5f - edge * 0.5f, -0.12f), new Vector3(full, edge, 1f), WithAlpha(baseTint, edgeAlpha));
            ConfigureIcePart(overlay.transform, "IceBottom", new Vector3(0f, -full * 0.5f + edge * 0.5f, -0.12f), new Vector3(full, edge, 1f), WithAlpha(baseTint, edgeAlpha));
            ConfigureIcePart(overlay.transform, "IceLeft", new Vector3(-full * 0.5f + edge * 0.5f, 0f, -0.12f), new Vector3(edge, full, 1f), WithAlpha(baseTint, edgeAlpha));
            ConfigureIcePart(overlay.transform, "IceRight", new Vector3(full * 0.5f - edge * 0.5f, 0f, -0.12f), new Vector3(edge, full, 1f), WithAlpha(baseTint, edgeAlpha));
        }

        private void ConfigureIcePart(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color tint)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Quad);
                part.name = name;
                part.transform.SetParent(parent);
                var collider = part.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                var renderer = part.GetComponent<MeshRenderer>();
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                child = part.transform;
            }

            child.localPosition = localPosition;
            child.localScale = localScale;
            var meshRenderer = child.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.material.mainTexture = iceTexture;
                meshRenderer.material.color = iceTexture == null ? new Color(tint.r, tint.g, tint.b, Mathf.Min(tint.a, 0.55f)) : tint;
            }
        }

        private Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
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

        private void ShowRewardedAd(Action onSuccess)
        {
            onSuccess?.Invoke();
        }

        private bool CanUseInLevelAd()
        {
            return !inputLocked &&
                   !upgradePanelOpen &&
                   !AreLevelTargetsCleared() &&
                   movesRemaining > 0 &&
                   board != null;
        }

        private void OnExtraSkillAdClicked()
        {
            if (extraSkillAdUsedThisRun || !CanUseInLevelAd())
            {
                return;
            }

            ShowRewardedAd(() =>
            {
                extraSkillAdUsedThisRun = true;
                inputLocked = true;
                ShowUpgradeChoices(Mathf.Clamp(room - 1, 0, RoomsPerRun - 1), false);
            });
        }

        private void OnExtraMovesAdClicked()
        {
            if (extraMovesAdUsedThisLevel || !CanUseInLevelAd())
            {
                return;
            }

            ShowRewardedAd(() =>
            {
                extraMovesAdUsedThisLevel = true;
                extraMovesAdUseCount++;
                movesRemaining += 5;
                RefreshStatus();
                RefreshAdButtons();
            });
        }

        private void OnShuffleAdClicked()
        {
            if (shuffleAdUsedThisLevel || !CanUseInLevelAd())
            {
                return;
            }

            ShowRewardedAd(() =>
            {
                shuffleAdUsedThisLevel = true;
                shuffleAdUseCount++;
                inputLocked = true;
                RefreshAdButtons();
                StartCoroutine(ShuffleMovableTilesByAdRoutine());
            });
        }

        private IEnumerator ShuffleMovableTilesByAdRoutine()
        {
            ShowTriggerText("广告洗牌！", Color.white);
            var moves = ShuffleMovableTiles();
            RefreshBoardTransforms();
            if (moves.Count > 0)
            {
                yield return AnimateFalls(moves);
                yield return new WaitForSeconds(CascadePauseSeconds);
            }

            if (!HasAvailableMove())
            {
                yield return EnsurePlayableBoardRoutine();
            }

            inputLocked = false;
            MarkEffectiveAction();
            RefreshStatus();
            RefreshAdButtons();
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

            SpendMove();
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
            SpendMove();

            var clearSet = new HashSet<Vector2Int> { pos };
            var manualSpecial = board[pos.x, pos.y].Special;
            PendingSpecial? aftershock = GetManualAftershock(pos, manualSpecial);
            if (manualSpecial == SpecialKind.Rainbow)
            {
                AddRainbowColorClear(GetMostCommonTileType(), clearSet);
                ApplyRainbowBonusClears(pos, clearSet);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, aftershock);
            return true;
        }

        private void SwapTiles(Vector2Int a, Vector2Int b)
        {
            (board[a.x, a.y], board[b.x, b.y]) = (board[b.x, b.y], board[a.x, a.y]);
            board[a.x, a.y].Object.transform.position = GridToWorld(a.x, a.y);
            board[b.x, b.y].Object.transform.position = GridToWorld(b.x, b.y);
        }

        private void SpendMove()
        {
            movesRemaining = Mathf.Max(0, movesRemaining - 1);
            totalMovesUsed++;
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
            SpendMove();

            if (first.Special != SpecialKind.None && second.Special != SpecialKind.None)
            {
                ResolveSpecialCombination(a, b, first.Special, second.Special);
                return true;
            }

            var specialPos = first.Special != SpecialKind.None ? a : b;
            var normalPos = first.Special != SpecialKind.None ? b : a;
            var manualSpecial = first.Special != SpecialKind.None ? first.Special : second.Special;
            var clearSet = new HashSet<Vector2Int> { specialPos };
            var matches = FindMatchGroups();
            foreach (var matchPos in matches.SelectMany(group => group.Positions))
            {
                clearSet.Add(matchPos);
            }

            var specialToCreate = DetermineSpecialKind(matches, GetPreferredSpecialSpawn(a, b, matches));
            var aftershock = GetManualAftershock(specialPos, manualSpecial);
            if (aftershock.HasValue)
            {
                specialToCreate = aftershock;
            }

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
                rainbowActivationCount += 2;
                AddEntireBoard(clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false, 2);
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
                rocketActivationCount += 2;
                AddRow(b.y, clearSet);
                AddColumn(b.x, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false, 2);
                return;
            }

            if (firstSpecial == SpecialKind.Bomb && secondSpecial == SpecialKind.Bomb)
            {
                bombActivationCount += 2;
                AddRadius(b, 3, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false, 2);
                return;
            }

            if ((firstIsRocket && secondSpecial == SpecialKind.Bomb) || (secondIsRocket && firstSpecial == SpecialKind.Bomb))
            {
                rocketActivationCount++;
                bombActivationCount++;
                AddStrongRocketBombClear(b, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false, 2);
                return;
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
        }

        private PendingSpecial? GetManualAftershock(Vector2Int pos, SpecialKind special)
        {
            if (special == SpecialKind.Bomb && HasUpgrade(UpgradeKind.ExplosionAftershock))
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.ExplosionAftershock));
                return new PendingSpecial(pos, RollRocketSpecial());
            }

            if (IsRocket(special) && HasUpgrade(UpgradeKind.RocketAftershock))
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RocketAftershock));
                return new PendingSpecial(pos, SpecialKind.Propeller);
            }

            if (special == SpecialKind.Rainbow && HasUpgrade(UpgradeKind.RainbowAftershock))
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RainbowAftershock));
                return new PendingSpecial(pos, SpecialKind.Bomb);
            }

            return null;
        }

        private void ResolvePropellerCombination(Vector2Int propellerPos, Vector2Int partnerPos, SpecialKind partnerSpecial, HashSet<Vector2Int> clearSet)
        {
            propellerActivationCount++;
            var reservedTargets = new HashSet<Vector2Int> { propellerPos, partnerPos };
            var mode = GetPropellerTargetMode(partnerSpecial);
            var target = GetSmartPropellerTarget(mode, reservedTargets);
            reservedTargets.Add(target);
            AddCross(propellerPos, clearSet);
            AddPropellerBoostArea(propellerPos, GetUpgradeLevel(UpgradeKind.PropellerBoost), clearSet);

            if (partnerSpecial == SpecialKind.Bomb)
            {
                AddBombClear(target, clearSet);
            }
            else if (IsRocket(partnerSpecial))
            {
                rocketActivationCount++;
                if (partnerSpecial == SpecialKind.LineHorizontal)
                {
                    AddRow(target.y, clearSet);
                }
                else
                {
                    AddColumn(target.x, clearSet);
                }

                if (GetUpgradeLevel(UpgradeKind.RocketSplit) > 0)
                {
                    ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RocketSplit));
                    AddWideRocketClear(target, partnerSpecial == SpecialKind.LineHorizontal ? MatchOrientation.Horizontal : MatchOrientation.Vertical, clearSet);
                }
            }
            else if (partnerSpecial == SpecialKind.Propeller)
            {
                propellerActivationCount++;
                var targetCount = HasUpgrade(UpgradeKind.PropellerCore) ? 4 : 3;
                for (var i = 0; i < targetCount; i++)
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
            ResolveClearSet(clearSet, null, false, 2);
        }

        private void ResolveRainbowSpecialCombination(SpecialKind targetSpecial, HashSet<Vector2Int> clearSet)
        {
            rainbowActivationCount++;
            var targetType = GetMostCommonTileType();
            var maxCount = targetSpecial == SpecialKind.Bomb ? 9 : IsRocket(targetSpecial) ? 14 : int.MaxValue;
            var positions = GetPrioritizedTilesOfType(targetType, maxCount);
            var specialToApply = IsRocket(targetSpecial)
                ? (rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical)
                : targetSpecial;

            if (targetSpecial == SpecialKind.Bomb)
            {
                bombActivationCount += positions.Count;
            }
            else if (IsRocket(targetSpecial))
            {
                rocketActivationCount += positions.Count;
            }
            else if (targetSpecial == SpecialKind.Propeller)
            {
                propellerActivationCount += positions.Count;
                ResolveRainbowPropellerCombination(positions, clearSet);
                return;
            }

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

                    if (GetUpgradeLevel(UpgradeKind.RocketSplit) > 0)
                    {
                        ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RocketSplit));
                        AddWideRocketClear(pos, specialToApply == SpecialKind.LineHorizontal ? MatchOrientation.Horizontal : MatchOrientation.Vertical, clearSet);
                    }
                }
                else
                {
                    clearSet.Add(pos);
                }
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null, false, 2);
        }

        private void ResolveRainbowPropellerCombination(List<Vector2Int> propellerPositions, HashSet<Vector2Int> clearSet)
        {
            var reservedTargets = new HashSet<Vector2Int>(clearSet);
            foreach (var pos in propellerPositions)
            {
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                clearSet.Add(pos);
                reservedTargets.Add(pos);
                var target = GetSmartPropellerTarget(PropellerTargetMode.Single, reservedTargets);
                reservedTargets.Add(target);
                clearSet.Add(target);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null, false, 2);
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
            else if (HasIce(target))
            {
                score = 105f + iceHealth[target.x, target.y] * 30f;
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
            AddRadius(target, 2, affected);
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
            var candidates = GetAvailableTileTypes().ToList();
            if (candidates.Count == 0)
            {
                candidates = Enumerable.Range(0, currentColorCount).ToList();
            }

            return candidates
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

            foreach (var pos in positions)
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

            var crateHits = CountTargetsAffectedByMatches(matchedPositions);
            var score = 10f + matchedPositions.Count;
            if (crateHits > 0)
            {
                score += 1000f + crateHits * 120f;
            }

            if (special.HasValue)
            {
                score += GetSpecialHintValue(special.Value.Special);
                score += GetBuildHintBonus(special.Value.Special);
                score += CountTargetsAffectedBySpecialPreview(special.Value.Position, special.Value.Special) * 50f;
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
            score += CountTargetsAffectedBySpecialPreview(a, firstSpecial) * 140f;
            score += CountTargetsAffectedBySpecialPreview(b, secondSpecial) * 140f;
            return score;
        }

        private int CountTargetsAffectedByMatches(HashSet<Vector2Int> matchedPositions)
        {
            var targetHits = new HashSet<Vector2Int>();
            foreach (var pos in matchedPositions)
            {
                if (HasIce(pos))
                {
                    targetHits.Add(pos);
                }

                AddAdjacentCrates(pos, targetHits);
            }

            return targetHits.Count;
        }

        private int CountTargetsAffectedBySpecialPreview(Vector2Int pos, SpecialKind special)
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
                AddRow(pos.y, affected);
            }
            else if (special == SpecialKind.LineVertical)
            {
                AddColumn(pos.x, affected);
            }
            else if (special == SpecialKind.Propeller)
            {
                var target = GetSmartPropellerTarget(PropellerTargetMode.Single, new HashSet<Vector2Int> { pos });
                affected.Add(target);
                AddCross(pos, affected);
            }

            return affected.Count(HasTargetAt);
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

        private List<TileMove> ShuffleMovableTiles()
        {
            var positions = new List<Vector2Int>();
            var tiles = new List<Tile>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (IsMovableTile(board[x, y]))
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
            if (upgradePanelOpen || AreLevelTargetsCleared())
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
            bestComboChain = Mathf.Max(bestComboChain, comboChain);
        }

        private void RecordClearPerformance(int clearCount)
        {
            maxSingleClearCount = Mathf.Max(maxSingleClearCount, clearCount);
        }

        private void ResolveClearSet(HashSet<Vector2Int> baseClears, PendingSpecial? specialToCreate, bool expandSpecials = true, int initialTriggeredSpecialCount = 0)
        {
            StartCoroutine(ResolveClearSetRoutine(baseClears, specialToCreate, expandSpecials, initialTriggeredSpecialCount));
        }

        private IEnumerator ResolveClearSetRoutine(HashSet<Vector2Int> baseClears, PendingSpecial? specialToCreate, bool expandSpecials = true, int initialTriggeredSpecialCount = 0)
        {
            var currentClears = baseClears;
            var currentSpecial = specialToCreate;
            var currentExpandSpecials = expandSpecials;

            while (true)
            {
                yield return new WaitForSeconds(MatchPauseSeconds);

                var bonusClears = new HashSet<Vector2Int>(currentClears);
                var triggeredSpecialCount = initialTriggeredSpecialCount;
                initialTriggeredSpecialCount = 0;
                if (currentExpandSpecials)
                {
                    triggeredSpecialCount += ExpandSpecialClears(currentClears, bonusClears);
                }

                if (currentSpecial.HasValue)
                {
                    bonusClears.Remove(currentSpecial.Value.Position);
                }

                var normalClearedCount = CountNormalColorClears(bonusClears);
                DamageTargetsForClears(currentClears, bonusClears);
                RecordClearPerformance(bonusClears.Count);
                yield return AnimateAndRemoveClears(bonusClears);

                if (currentSpecial.HasValue)
                {
                    CreateSpecialTileAt(currentSpecial.Value);
                }

                foreach (var pendingSpecial in pendingPostClearSpecials)
                {
                    CreateSpecialTileAt(pendingSpecial);
                }

                pendingPostClearSpecials.Clear();

                var upgradeClears = ApplyPostClearUpgradeSpawns(normalClearedCount, triggeredSpecialCount);
                if (upgradeClears.Count > 0)
                {
                    DamageTargetsForClears(upgradeClears, upgradeClears);
                    RecordClearPerformance(upgradeClears.Count);
                    yield return AnimateAndRemoveClears(upgradeClears);
                }

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

            if (AreLevelTargetsCleared())
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

        private void DamageTargetsForClears(HashSet<Vector2Int> directClears, HashSet<Vector2Int> finalClears)
        {
            var targetHits = new HashSet<Vector2Int>();
            foreach (var pos in finalClears)
            {
                targetHits.Add(pos);
            }

            foreach (var pos in directClears)
            {
                AddAdjacentCrates(pos, targetHits);
            }

            foreach (var pos in targetHits)
            {
                if (!IsInside(pos))
                {
                    continue;
                }

                var wasScheduledToClear = finalClears.Contains(pos);
                crateDamageBonuses.TryGetValue(pos, out var bonusDamage);
                var hitCrate = board[pos.x, pos.y] != null && board[pos.x, pos.y].CrateHealth > 0;
                if (hitCrate)
                {
                    var destroyed = DamageCrate(pos, 1 + bonusDamage);
                    if (!destroyed || HasIce(pos))
                    {
                        finalClears.Remove(pos);
                        if (destroyed && board[pos.x, pos.y] != null)
                        {
                            DecorateTile(board[pos.x, pos.y]);
                        }
                    }
                    else if (!wasScheduledToClear && board[pos.x, pos.y] != null)
                    {
                        DecorateTile(board[pos.x, pos.y]);
                    }

                    RefreshIceOverlay(pos);
                    continue;
                }

                if (HasIce(pos))
                {
                    DamageIce(pos, 1 + bonusDamage);
                }
            }

            crateDamageBonuses.Clear();
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

        private bool DamageCrate(Vector2Int pos, int damage)
        {
            ClearSelectionIfAt(pos);
            var tile = board[pos.x, pos.y];
            var actualDamage = Mathf.Min(tile.CrateHealth, Mathf.Max(1, damage));
            tile.CrateHealth -= actualDamage;
            boxDamageTotal += actualDamage;
            if (tile.CrateHealth <= 0)
            {
                tile.Object.transform.localScale = Vector3.one * tileScale;
                remainingCrates = Mathf.Max(0, remainingCrates - 1);
                clearedBoxCount++;
                StartCoroutine(AnimateCrateBreak(tile));
                return true;
            }

            StartCoroutine(AnimateCrateHit(tile));
            DecorateTile(tile);
            return false;
        }

        private bool DamageIce(Vector2Int pos, int damage)
        {
            if (!HasIce(pos))
            {
                return false;
            }

            var actualDamage = Mathf.Min(iceHealth[pos.x, pos.y], Mathf.Max(1, damage));
            iceHealth[pos.x, pos.y] -= actualDamage;
            iceDamageTotal += actualDamage;
            if (iceHealth[pos.x, pos.y] <= 0)
            {
                iceHealth[pos.x, pos.y] = 0;
                remainingIce = Mathf.Max(0, remainingIce - 1);
                clearedIceCount++;
                RefreshIceOverlay(pos);
                return true;
            }

            RefreshIceOverlay(pos);
            return false;
        }

        private bool HasIce(Vector2Int pos)
        {
            return IsInside(pos) && iceHealth[pos.x, pos.y] > 0;
        }

        private bool HasTargetAt(Vector2Int pos)
        {
            return IsInside(pos) &&
                   ((board[pos.x, pos.y] != null && board[pos.x, pos.y].CrateHealth > 0) || HasIce(pos));
        }

        private bool AreLevelTargetsCleared()
        {
            return remainingCrates <= 0 && remainingIce <= 0;
        }

        private void RegisterCrateDamageBonus(IEnumerable<Vector2Int> positions, int bonus)
        {
            if (bonus <= 0)
            {
                return;
            }

            foreach (var pos in positions)
            {
                if (!IsInside(pos))
                {
                    continue;
                }

                crateDamageBonuses[pos] = Mathf.Max(crateDamageBonuses.TryGetValue(pos, out var current) ? current : 0, bonus);
            }
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

        private void AddWideRocketClear(Vector2Int center, MatchOrientation orientation, HashSet<Vector2Int> output)
        {
            for (var offset = -1; offset <= 1; offset++)
            {
                if (orientation == MatchOrientation.Horizontal)
                {
                    AddRow(Mathf.Clamp(center.y + offset, 0, Height - 1), output);
                }
                else
                {
                    AddColumn(Mathf.Clamp(center.x + offset, 0, Width - 1), output);
                }
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

        private int CountNormalColorClears(HashSet<Vector2Int> clearedPositions)
        {
            return clearedPositions.Count(pos => IsInside(pos) && IsColorTile(board[pos.x, pos.y]));
        }

        private HashSet<Vector2Int> ApplyPostClearUpgradeSpawns(int normalClearedCount, int triggeredSpecialCount)
        {
            var upgradeClears = new HashSet<Vector2Int>();
            TryCreateSpecialByClearProgress(UpgradeKind.ExplosionCore, ref bombCoreClearProgress, normalClearedCount, 20, 20, 20, () => SpecialKind.Bomb);
            TryCreateSpecialByClearProgress(UpgradeKind.RocketCore, ref rocketCoreClearProgress, normalClearedCount, 14, 14, 14, RollRocketSpecial);
            TryCreateSpecialByClearProgress(UpgradeKind.RainbowCore, ref rainbowCoreClearProgress, normalClearedCount, 25, 25, 25, () => SpecialKind.Rainbow);
            TryCreateSpecialByClearProgress(UpgradeKind.BombSpawn, ref bombSpawnClearProgress, triggeredSpecialCount, 12, 10, 8, () => SpecialKind.Bomb);
            TryCreateSpecialByClearProgress(UpgradeKind.RocketSpawn, ref rocketSpawnClearProgress, triggeredSpecialCount, 10, 8, 6, RollRocketSpecial);
            TryCreateSpecialByClearProgress(UpgradeKind.RainbowSpawn, ref rainbowSpawnClearProgress, triggeredSpecialCount, 15, 13, 10, () => SpecialKind.Rainbow);
            TryCreateSpecialByClearProgress(UpgradeKind.PropellerSpawn, ref propellerSpawnClearProgress, triggeredSpecialCount, 8, 6, 4, () => SpecialKind.Propeller);
            TryCreateSpecialByClearProgress(UpgradeKind.PropellerRebirth, ref propellerRebirthMatchProgress, normalClearedCount, 8, 8, 8, () => SpecialKind.Propeller);
            TryAddClearEffectByProgress(UpgradeKind.EdgeWalker, ref edgeWalkerClearProgress, normalClearedCount, 12, 10, 8, upgradeClears, AddEdgeColumns);
            TryAddClearEffectByProgress(UpgradeKind.BottomSweep, ref bottomSweepClearProgress, normalClearedCount, 14, 12, 10, upgradeClears, AddBottomRows);
            return upgradeClears;
        }

        private void TryAddClearEffectByProgress(UpgradeKind kind, ref int clearProgress, int clearedCount, int levelOneThreshold, int levelTwoThreshold, int levelThreeThreshold, HashSet<Vector2Int> output, Action<HashSet<Vector2Int>> addEffect)
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

                addEffect(output);
            }
        }

        private void AddEdgeColumns(HashSet<Vector2Int> output)
        {
            AddColumn(0, output);
            AddColumn(Width - 1, output);
        }

        private void AddBottomRows(HashSet<Vector2Int> output)
        {
            AddRow(0, output);
            AddRow(Mathf.Min(1, Height - 1), output);
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
            var bombCount = (HasUpgrade(UpgradeKind.ExplosionCore) ? 2 : 0) + GetUpgradeLevel(UpgradeKind.BombReserve) * 2;
            for (var i = 0; i < bombCount; i++)
            {
                CreateSpecialOnRandomColorTile(SpecialKind.Bomb);
            }

            var rainbowCount = (HasUpgrade(UpgradeKind.RainbowCore) ? 1 : 0) + GetUpgradeLevel(UpgradeKind.RainbowReserve);
            for (var i = 0; i < rainbowCount; i++)
            {
                CreateSpecialOnRandomColorTile(SpecialKind.Rainbow);
            }

            var rocketCount = (HasUpgrade(UpgradeKind.RocketCore) ? 3 : 0) + GetReserveSpawnCount(UpgradeKind.RocketReserve, 3, 5);
            for (var i = 0; i < rocketCount; i++)
            {
                CreateSpecialOnRandomColorTile(RollRocketSpecial());
            }

            var propellerCount = (HasUpgrade(UpgradeKind.PropellerCore) ? 4 : 0) + GetReserveSpawnCount(UpgradeKind.PropellerReserve, 4, 6);
            for (var i = 0; i < propellerCount; i++)
            {
                CreateSpecialOnRandomColorTile(SpecialKind.Propeller);
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

        private int GetReserveSpawnCount(UpgradeKind kind, int levelOneCount, int levelTwoCount)
        {
            var level = GetUpgradeLevel(kind);
            if (level <= 0)
            {
                return 0;
            }

            return level == 1 ? levelOneCount : levelTwoCount;
        }

        private int ExpandSpecialClears(HashSet<Vector2Int> baseClears, HashSet<Vector2Int> output)
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

            return expandedSpecials.Count;
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
            if (orientation == MatchOrientation.Horizontal)
            {
                AddRow(pos.y, output);
            }
            else
            {
                AddColumn(pos.x, output);
            }

            if (GetUpgradeLevel(UpgradeKind.RocketSplit) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RocketSplit));
                AddWideRocketClear(pos, orientation, output);
            }

            RegisterCrateDamageBonus(output, GetUpgradeLevel(UpgradeKind.RocketDamage));
        }

        private void AddBombClear(Vector2Int pos, HashSet<Vector2Int> output)
        {
            bombActivationCount++;
            var radius = 2;
            var damageBonus = GetUpgradeLevel(UpgradeKind.BombDamage);
            if (damageBonus > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.BombDamage));
            }

            var affected = new HashSet<Vector2Int>();
            AddRadius(pos, radius, affected);
            RegisterCrateDamageBonus(affected, damageBonus);
            AddRadius(pos, radius, output);
        }

        private void ApplyRainbowBonusClears(Vector2Int pos, HashSet<Vector2Int> output)
        {
            rainbowActivationCount++;
            var mutationLevel = GetUpgradeLevel(UpgradeKind.RainbowMutation);
            if (mutationLevel > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.RainbowMutation));
                var chance = mutationLevel == 1 ? 0.15f : mutationLevel == 2 ? 0.3f : 0.5f;
                foreach (var clearedPos in output.ToArray())
                {
                    if (clearedPos == pos || !IsInside(clearedPos) || !IsColorTile(board[clearedPos.x, clearedPos.y]))
                    {
                        continue;
                    }

                    if ((float)rng.NextDouble() <= chance)
                    {
                        pendingPostClearSpecials.Add(new PendingSpecial(clearedPos, SpecialKind.Propeller));
                    }
                }
            }
        }

        private void AddPropellerClear(Vector2Int pos, HashSet<Vector2Int> output)
        {
            propellerActivationCount++;
            var reservedTargets = new HashSet<Vector2Int>(output) { pos };
            var target = GetSmartPropellerTarget(PropellerTargetMode.Single, reservedTargets);
            reservedTargets.Add(target);
            AddCross(pos, output);
            AddPropellerBoostArea(pos, GetUpgradeLevel(UpgradeKind.PropellerBoost), output);
            output.Add(target);
            if (HasUpgrade(UpgradeKind.PropellerCore))
            {
                var coreTarget = GetSmartPropellerTarget(PropellerTargetMode.Multi, reservedTargets);
                reservedTargets.Add(coreTarget);
                output.Add(coreTarget);
            }

            if (GetUpgradeLevel(UpgradeKind.PropellerBoost) > 0)
            {
                ShowUpgradeTrigger(GetUpgradeDefinition(UpgradeKind.PropellerBoost));
            }

            RegisterCrateDamageBonus(output, GetUpgradeLevel(UpgradeKind.PropellerDamage));
        }

        private void AddPropellerBoostArea(Vector2Int center, int level, HashSet<Vector2Int> output)
        {
            if (level <= 0)
            {
                return;
            }

            var reach = Mathf.Clamp(level + 1, 2, 3);
            output.Add(center);
            for (var offset = 1; offset <= reach; offset++)
            {
                var positions = new[]
                {
                    new Vector2Int(center.x + offset, center.y),
                    new Vector2Int(center.x - offset, center.y),
                    new Vector2Int(center.x, center.y + offset),
                    new Vector2Int(center.x, center.y - offset)
                };

                foreach (var pos in positions)
                {
                    if (IsInside(pos))
                    {
                        output.Add(pos);
                    }
                }
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
                board[pos.x, pos.y] = CreateTile(pos.x, pos.y, rng.Next(currentColorCount));
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
            RecordCompletedRoomMoves();
            if (room >= RoomsPerRun)
            {
                ShowRunSummary(true, RoomsPerRun);
                return;
            }

            pendingRewardAfterRoom = room;
            room++;
            ShowUpgradeChoices(pendingRewardAfterRoom, true);
        }

        private void RecordCompletedRoomMoves()
        {
            var completedIndex = Mathf.Clamp(room - 1, 0, RoomsPerRun - 1);
            while (levelRemainingMoves.Count < completedIndex)
            {
                levelRemainingMoves.Add(0);
            }

            if (levelRemainingMoves.Count == completedIndex)
            {
                levelRemainingMoves.Add(movesRemaining);
            }
            else
            {
                levelRemainingMoves[completedIndex] = movesRemaining;
            }

            runScore = levelRemainingMoves.Sum();
        }

        private void ShowUpgradeChoices(int completedRoom, bool startsRoomAfterSelection)
        {
            summaryPanel.gameObject.SetActive(false);
            currentUpgradeChoiceCompletedRoom = completedRoom;
            choiceStartsRoomAfterSelection = startsRoomAfterSelection;
            for (var i = 0; i < optionRefreshUsed.Length; i++)
            {
                optionRefreshUsed[i] = false;
            }

            ConfigureUpgradeTextForChoices();
            upgradeText.text = completedRoom <= 0
                ? "开局选择\n选择一个技能进入第 1 关"
                : startsRoomAfterSelection
                    ? $"第 {completedRoom} 关完成\n选择一个技能进入第 {room} 关"
                    : "广告额外选择\n选择一个技能后继续当前关";

            SetUpgradePanel(true);
            currentUpgradeChoices.Clear();
            displayedUpgradeKindsThisChoice.Clear();
            currentUpgradeChoices.AddRange(RollUpgradeChoices(completedRoom));
            foreach (var choice in currentUpgradeChoices)
            {
                displayedUpgradeKindsThisChoice.Add(choice.Kind);
            }
            RefreshUpgradeChoiceButtons();
            RefreshAdButtons();
        }

        private void RefreshUpgradeChoiceButtons()
        {
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                if (i >= currentUpgradeChoices.Count)
                {
                    upgradeButtons[i].gameObject.SetActive(false);
                    upgradeRefreshButtons[i].gameObject.SetActive(false);
                    continue;
                }

                var upgrade = currentUpgradeChoices[i];
                var button = upgradeButtons[i];
                button.gameObject.SetActive(true);
                button.GetComponent<Image>().color = GetFactionColor(upgrade.Faction);
                button.GetComponentInChildren<Text>().text = $"{GetRarityLabel(upgrade.Rarity)} {GetFactionLabel(upgrade.Faction)} {GetUpgradeDisplayName(upgrade)}\n{GetUpgradeDisplayDescription(upgrade)}";
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    var needsBoardSettle = ApplyUpgradeSelection(upgrade);
                    SetUpgradePanel(false);
                    RefreshAdButtons();
                    if (choiceStartsRoomAfterSelection)
                    {
                        StartRoom();
                    }
                    else if (needsBoardSettle)
                    {
                        StartCoroutine(SettleBoardAfterImmediateUpgradeRoutine());
                    }
                    else
                    {
                        inputLocked = false;
                        RefreshStatus();
                    }
                });

                var refreshButton = upgradeRefreshButtons[i];
                refreshButton.gameObject.SetActive(!optionRefreshUsed[i]);
                refreshButton.interactable = !optionRefreshUsed[i];
                refreshButton.onClick.RemoveAllListeners();
                var optionIndex = i;
                refreshButton.onClick.AddListener(() => OnRefreshUpgradeOptionAdClicked(optionIndex));
            }
        }

        private bool ApplyUpgradeSelection(RogueUpgrade upgrade)
        {
            activeUpgrades.Add(upgrade);
            if (IsColorRemovalUpgrade(upgrade.Kind))
            {
                removedTileTypes.Add(GetRemovedTileType(upgrade.Kind));
                return ClearRemovedColorTiles(GetRemovedTileType(upgrade.Kind));
            }

            return false;
        }

        private string GetUpgradeDisplayName(RogueUpgrade upgrade)
        {
            var nextLevel = GetUpgradeLevel(upgrade.Kind) + 1;
            return upgrade.MaxLevel > 1 ? $"{upgrade.Name}{nextLevel}" : upgrade.Name;
        }

        private string GetUpgradeDisplayDescription(RogueUpgrade upgrade)
        {
            var nextLevel = Mathf.Clamp(GetUpgradeLevel(upgrade.Kind) + 1, 1, upgrade.MaxLevel);
            switch (upgrade.Kind)
            {
                case UpgradeKind.BombDamage:
                    return $"炸弹触发时，对障碍物的伤害+{nextLevel}。";
                case UpgradeKind.BombSpawn:
                    return $"累计触发{GetLevelValue(nextLevel, 12, 10, 8)}个特效后生成炸弹。";
                case UpgradeKind.BombReserve:
                    return $"每关开始时额外生成{GetLevelValue(nextLevel, 2, 4, 4)}个炸弹。";
                case UpgradeKind.RocketReserve:
                    return $"每关开始时额外生成{GetLevelValue(nextLevel, 3, 5, 5)}个火箭。";
                case UpgradeKind.RocketDamage:
                    return $"火箭触发时，对障碍物的伤害+{nextLevel}。";
                case UpgradeKind.RocketSpawn:
                    return $"累计触发{GetLevelValue(nextLevel, 10, 8, 6)}个特效后生成火箭。";
                case UpgradeKind.RainbowSpawn:
                    return $"累计触发{GetLevelValue(nextLevel, 15, 13, 10)}个特效后生成彩球。";
                case UpgradeKind.RainbowReserve:
                    return $"每关开始时额外生成{GetLevelValue(nextLevel, 1, 2, 2)}个彩球。";
                case UpgradeKind.RainbowMutation:
                    return $"彩球触发后，被消除的方块处有{GetLevelValue(nextLevel, 15, 30, 50)}%概率生成螺旋桨。";
                case UpgradeKind.PropellerSpawn:
                    return $"累计触发{GetLevelValue(nextLevel, 8, 6, 4)}个特效后生成螺旋桨。";
                case UpgradeKind.PropellerReserve:
                    return $"每关开始时额外生成{GetLevelValue(nextLevel, 4, 6, 6)}个螺旋桨。";
                case UpgradeKind.PropellerDamage:
                    return $"螺旋桨触发时，对障碍物的伤害+{nextLevel}。";
                case UpgradeKind.PropellerBoost:
                    return $"螺旋桨原地爆炸时的范围扩大为横纵方向的{GetLevelValue(nextLevel, 2, 3, 3)}格。";
                case UpgradeKind.EdgeWalker:
                    return $"每累计完成{GetLevelValue(nextLevel, 12, 10, 8)}次普通消除，对左右边缘列进行一次消除。";
                case UpgradeKind.BottomSweep:
                    return $"每累计完成{GetLevelValue(nextLevel, 14, 12, 10)}次普通消除，对最底部的两行进行一次消除。";
                default:
                    return upgrade.Description;
            }
        }

        private int GetLevelValue(int level, int levelOne, int levelTwo, int levelThree)
        {
            return level <= 1 ? levelOne : level == 2 ? levelTwo : levelThree;
        }

        private void OnRefreshUpgradeOptionAdClicked(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex >= currentUpgradeChoices.Count || optionRefreshUsed[optionIndex])
            {
                return;
            }

            ShowRewardedAd(() =>
            {
                var replacement = RollReplacementUpgradeChoice(optionIndex);
                if (string.IsNullOrEmpty(replacement.Name))
                {
                    ShowTriggerText("没有可刷新技能", Color.white);
                    RefreshUpgradeChoiceButtons();
                    return;
                }

                currentUpgradeChoices[optionIndex] = replacement;
                displayedUpgradeKindsThisChoice.Add(replacement.Kind);
                optionRefreshUsed[optionIndex] = true;
                skillRefreshAdUseCount++;
                RefreshUpgradeChoiceButtons();
            });
        }

        private bool ClearRemovedColorTiles(int tileType)
        {
            if (board == null)
            {
                return false;
            }

            var removedAny = false;
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var tile = board[x, y];
                    if (tile == null || tile.Type != tileType || tile.Special != SpecialKind.None || tile.CrateHealth > 0)
                    {
                        continue;
                    }

                    if (tile.Object != null)
                    {
                        Destroy(tile.Object);
                    }

                    board[x, y] = null;
                    removedAny = true;
                }
            }

            return removedAny;
        }

        private IEnumerator SettleBoardAfterImmediateUpgradeRoutine()
        {
            inputLocked = true;
            RefreshStatus();
            var fallMoves = ApplyGravity();
            var spawnMoves = RefillBoard();
            fallMoves.AddRange(spawnMoves);
            yield return AnimateFalls(fallMoves);
            yield return EnsurePlayableBoardRoutine();
            inputLocked = false;
            MarkEffectiveAction();
            RefreshStatus();
        }

        private RogueUpgrade[] RollUpgradeChoices(int completedRoom)
        {
            var pool = GetAvailableUpgradePool(completedRoom);

            var choices = new List<RogueUpgrade>();
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                var rarity = RollChoiceRarity(completedRoom);
                var picked = PickUpgradeForRarity(pool, choices, rarity);
                if (!string.IsNullOrEmpty(picked.Name))
                {
                    choices.Add(picked);
                }
            }

            ApplyUpgradeChoiceGuarantees(completedRoom, pool, choices);
            UpdateCoreFactionChoiceMemory(choices);
            return choices.ToArray();
        }

        private RogueUpgrade RollReplacementUpgradeChoice(int optionIndex)
        {
            var pool = GetAvailableUpgradePool(currentUpgradeChoiceCompletedRoom);
            return PickUpgradeForRarityExcludingKinds(pool, displayedUpgradeKindsThisChoice, RollChoiceRarity(currentUpgradeChoiceCompletedRoom));
        }

        private RogueUpgrade PickUpgradeForRarityExcludingKinds(List<RogueUpgrade> pool, HashSet<UpgradeKind> excludedKinds, UpgradeRarity rarity)
        {
            foreach (var candidateRarity in GetRarityFallbacks(rarity))
            {
                var rarityPool = pool
                    .Where(upgrade => upgrade.Rarity == candidateRarity)
                    .Where(upgrade => !excludedKinds.Contains(upgrade.Kind))
                    .ToList();
                if (rarityPool.Count > 0)
                {
                    return PickWeightedUpgrade(rarityPool);
                }
            }

            var fallbackPool = pool
                .Where(upgrade => !excludedKinds.Contains(upgrade.Kind))
                .ToList();
            return fallbackPool.Count > 0 ? PickWeightedUpgrade(fallbackPool) : default;
        }

        private List<RogueUpgrade> GetAvailableUpgradePool(int completedRoom)
        {
            return GetUpgradePool()
                .Where(upgrade => GetUpgradeLevel(upgrade.Kind) < upgrade.MaxLevel)
                .Where(upgrade => completedRoom != 0 || upgrade.Rarity != UpgradeRarity.Epic)
                .Where(IsUpgradeUnlocked)
                .ToList();
        }

        private UpgradeRarity RollChoiceRarity(int completedRoom)
        {
            var roll = rng.NextDouble();
            switch (Mathf.Clamp(completedRoom, 0, RoomsPerRun - 1))
            {
                case 0:
                    return roll < 0.85 ? UpgradeRarity.Common : UpgradeRarity.Rare;
                case 1:
                    return roll < 0.75 ? UpgradeRarity.Common : UpgradeRarity.Rare;
                case 2:
                    return roll < 0.65 ? UpgradeRarity.Common : roll < 0.95 ? UpgradeRarity.Rare : UpgradeRarity.Epic;
                case 3:
                    return roll < 0.50 ? UpgradeRarity.Common : roll < 0.90 ? UpgradeRarity.Rare : UpgradeRarity.Epic;
                case 4:
                    return roll < 0.35 ? UpgradeRarity.Common : roll < 0.80 ? UpgradeRarity.Rare : UpgradeRarity.Epic;
                default:
                    return roll < 0.20 ? UpgradeRarity.Common : roll < 0.70 ? UpgradeRarity.Rare : UpgradeRarity.Epic;
            }
        }

        private RogueUpgrade PickUpgradeForRarity(List<RogueUpgrade> pool, List<RogueUpgrade> choices, UpgradeRarity rarity)
        {
            foreach (var candidateRarity in GetRarityFallbacks(rarity))
            {
                var rarityPool = pool
                    .Where(upgrade => upgrade.Rarity == candidateRarity)
                    .Where(upgrade => choices.All(choice => choice.Kind != upgrade.Kind))
                    .ToList();
                if (rarityPool.Count > 0)
                {
                    return PickWeightedUpgrade(rarityPool);
                }
            }

            var fallbackPool = pool
                .Where(upgrade => choices.All(choice => choice.Kind != upgrade.Kind))
                .ToList();
            return fallbackPool.Count > 0 ? PickWeightedUpgrade(fallbackPool) : default;
        }

        private IEnumerable<UpgradeRarity> GetRarityFallbacks(UpgradeRarity rarity)
        {
            if (rarity == UpgradeRarity.Epic)
            {
                yield return UpgradeRarity.Epic;
                yield return UpgradeRarity.Rare;
                yield return UpgradeRarity.Common;
                yield break;
            }

            if (rarity == UpgradeRarity.Rare)
            {
                yield return UpgradeRarity.Rare;
                yield return UpgradeRarity.Common;
                yield break;
            }

            yield return UpgradeRarity.Common;
        }

        private RogueUpgrade PickWeightedUpgrade(List<RogueUpgrade> pool)
        {
            var totalWeight = pool.Sum(GetUpgradeWeight);
            var roll = rng.NextDouble() * totalWeight;
            foreach (var upgrade in pool)
            {
                roll -= GetUpgradeWeight(upgrade);
                if (roll <= 0f)
                {
                    return upgrade;
                }
            }

            return pool[pool.Count - 1];
        }

        private void ApplyUpgradeChoiceGuarantees(int completedRoom, List<RogueUpgrade> pool, List<RogueUpgrade> choices)
        {
            if (completedRoom == 0)
            {
                EnsureOpeningCoreChoices(pool, choices);
            }

            if (completedRoom == 1)
            {
                EnsureEarlyBuildStarter(pool, choices);
            }

            if (completedRoom >= 4 && choices.All(upgrade => upgrade.Rarity == UpgradeRarity.Common))
            {
                var highRarityPool = pool
                    .Where(upgrade => upgrade.Rarity == UpgradeRarity.Rare || upgrade.Rarity == UpgradeRarity.Epic)
                    .Where(upgrade => choices.All(choice => choice.Kind != upgrade.Kind))
                    .ToList();
                if (highRarityPool.Count > 0)
                {
                    ReplaceChoice(choices, PickWeightedUpgrade(highRarityPool));
                }
            }
        }

        private void EnsureOpeningCoreChoices(List<RogueUpgrade> pool, List<RogueUpgrade> choices)
        {
            var missingCorePool = pool
                .Where(upgrade => upgrade.IsCore)
                .Where(upgrade => choices.All(choice => choice.Kind != upgrade.Kind))
                .ToList();

            if (choices.All(upgrade => !upgrade.IsCore) && missingCorePool.Count > 0)
            {
                var core = PickWeightedUpgrade(missingCorePool);
                ReplaceChoice(choices, core);
                missingCorePool.RemoveAll(upgrade => upgrade.Kind == core.Kind);
            }

            var distinctCoreCount = choices.Where(upgrade => upgrade.IsCore).Select(upgrade => upgrade.Faction).Distinct().Count();
            if (distinctCoreCount < 2 && missingCorePool.Count > 0)
            {
                ReplaceNonCoreChoice(choices, PickWeightedUpgrade(missingCorePool));
            }
        }

        private void EnsureEarlyBuildStarter(List<RogueUpgrade> pool, List<RogueUpgrade> choices)
        {
            var ownedCoreFactions = GetOwnedCoreFactions().ToList();
            if (ownedCoreFactions.Count == 0 ||
                choices.Any(upgrade => ownedCoreFactions.Contains(upgrade.Faction)) ||
                choices.Any(upgrade => upgrade.Faction == UpgradeFaction.General && upgrade.Rarity != UpgradeRarity.Common))
            {
                return;
            }

            var starterPool = pool
                .Where(upgrade => ownedCoreFactions.Contains(upgrade.Faction) || (upgrade.Faction == UpgradeFaction.General && upgrade.Rarity != UpgradeRarity.Common))
                .Where(upgrade => choices.All(choice => choice.Kind != upgrade.Kind))
                .ToList();
            if (starterPool.Count > 0)
            {
                ReplaceChoice(choices, PickWeightedUpgrade(starterPool));
            }
        }

        private void ReplaceNonCoreChoice(List<RogueUpgrade> choices, RogueUpgrade upgrade)
        {
            if (string.IsNullOrEmpty(upgrade.Name))
            {
                return;
            }

            var replaceIndex = choices.FindIndex(choice => !choice.IsCore);
            if (replaceIndex >= 0)
            {
                choices[replaceIndex] = upgrade;
                return;
            }

            if (choices.Count < upgradeButtons.Length)
            {
                choices.Add(upgrade);
                return;
            }

            var duplicateCoreIndex = choices
                .Select((choice, index) => new { choice, index })
                .GroupBy(item => item.choice.Faction)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Skip(1))
                .Select(item => item.index)
                .Cast<int?>()
                .FirstOrDefault();
            if (duplicateCoreIndex.HasValue)
            {
                choices[duplicateCoreIndex.Value] = upgrade;
            }
            else
            {
                ReplaceChoice(choices, upgrade);
            }
        }

        private void ReplaceChoice(List<RogueUpgrade> choices, RogueUpgrade upgrade)
        {
            if (string.IsNullOrEmpty(upgrade.Name))
            {
                return;
            }

            if (choices.Count < upgradeButtons.Length)
            {
                choices.Add(upgrade);
                return;
            }

            choices[rng.Next(choices.Count)] = upgrade;
        }

        private float GetUpgradeWeight(RogueUpgrade upgrade)
        {
            var weight = upgrade.IsCore ? 0.85f : 1f;
            if (upgrade.Faction == UpgradeFaction.General)
            {
                return weight;
            }

            if (HasFactionCore(upgrade.Faction))
            {
                weight *= 1.8f + Mathf.Max(0, GetFactionInvestment(upgrade.Faction) - 1) * 0.4f;
                if (missedCoreFactionChoiceCounts.TryGetValue(upgrade.Faction, out var missedCount) && missedCount >= 2)
                {
                    weight *= 1.5f;
                }
            }

            return weight;
        }

        private void UpdateCoreFactionChoiceMemory(List<RogueUpgrade> choices)
        {
            foreach (var faction in GetOwnedCoreFactions())
            {
                var sawFaction = choices.Any(upgrade => upgrade.Faction == faction);
                missedCoreFactionChoiceCounts[faction] = sawFaction
                    ? 0
                    : (missedCoreFactionChoiceCounts.TryGetValue(faction, out var current) ? current : 0) + 1;
            }
        }

        private IEnumerable<UpgradeFaction> GetOwnedCoreFactions()
        {
            return activeUpgrades
                .Where(upgrade => upgrade.IsCore)
                .Select(upgrade => upgrade.Faction)
                .Distinct();
        }

        private bool IsUpgradeUnlocked(RogueUpgrade upgrade)
        {
            if (IsColorRemovalUpgrade(upgrade.Kind))
            {
                return removedTileTypes.Count < 2 && !removedTileTypes.Contains(GetRemovedTileType(upgrade.Kind));
            }

            return upgrade.Faction == UpgradeFaction.General ||
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

        private bool IsColorRemovalUpgrade(UpgradeKind kind)
        {
            return kind == UpgradeKind.RemoveRed ||
                   kind == UpgradeKind.RemoveBlue ||
                   kind == UpgradeKind.RemoveYellow ||
                   kind == UpgradeKind.RemoveOrange ||
                   kind == UpgradeKind.RemovePurple ||
                   kind == UpgradeKind.RemoveGreen;
        }

        private int GetRemovedTileType(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.RemoveRed:
                    return 0;
                case UpgradeKind.RemoveBlue:
                    return 1;
                case UpgradeKind.RemoveGreen:
                    return 2;
                case UpgradeKind.RemoveYellow:
                    return 3;
                case UpgradeKind.RemovePurple:
                    return 4;
                case UpgradeKind.RemoveOrange:
                    return 5;
                default:
                    return -1;
            }
        }

        private RogueUpgrade[] GetUpgradePool()
        {
            return new[]
            {
                new RogueUpgrade(UpgradeKind.ExplosionCore, UpgradeFaction.Explosion, UpgradeRarity.Common, "爆破核心", "每累计完成20次普通消除，生成1个炸弹；每关开始生成2个炸弹。", 1, true),
                new RogueUpgrade(UpgradeKind.BombDamage, UpgradeFaction.Explosion, UpgradeRarity.Common, "炸弹扩容", "炸弹触发时，对障碍物的伤害+1/2。", 2, false),
                new RogueUpgrade(UpgradeKind.BombSpawn, UpgradeFaction.Explosion, UpgradeRarity.Common, "越炸越多", "累计触发12/10/8个特效后生成炸弹。", 3, false),
                new RogueUpgrade(UpgradeKind.BombReserve, UpgradeFaction.Explosion, UpgradeRarity.Rare, "炸弹储备", "每关开始时额外生成2/4个炸弹。", 2, false),
                new RogueUpgrade(UpgradeKind.ExplosionAftershock, UpgradeFaction.Explosion, UpgradeRarity.Rare, "爆炸余波", "炸弹被手动触发后，在触发处留下一个火箭。", 1, false),

                new RogueUpgrade(UpgradeKind.RocketCore, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭核心", "每累计完成14次普通消除，生成1个火箭；每关开始生成3个火箭。", 1, true),
                new RogueUpgrade(UpgradeKind.RocketReserve, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭储备", "每关开始时额外生成3/5个火箭。", 2, false),
                new RogueUpgrade(UpgradeKind.RocketDamage, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭扩容", "火箭触发时，对障碍物的伤害+1/2。", 2, false),
                new RogueUpgrade(UpgradeKind.RocketSpawn, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭补给", "累计触发10/8/6个特效后生成火箭。", 3, false),
                new RogueUpgrade(UpgradeKind.RocketAftershock, UpgradeFaction.Rocket, UpgradeRarity.Common, "火箭余波", "火箭被手动触发后，在触发处留下一个螺旋桨。", 1, false),
                new RogueUpgrade(UpgradeKind.RocketSplit, UpgradeFaction.Rocket, UpgradeRarity.Epic, "火箭分裂", "火箭范围加宽1格。", 1, false),

                new RogueUpgrade(UpgradeKind.RainbowCore, UpgradeFaction.Rainbow, UpgradeRarity.Rare, "彩虹核心", "每累计完成25次普通消除，生成1个彩球；每关开始生成1个彩球。", 1, true),
                new RogueUpgrade(UpgradeKind.RainbowSpawn, UpgradeFaction.Rainbow, UpgradeRarity.Rare, "彩虹凝结", "累计触发15/13/10个特效后生成彩球。", 3, false),
                new RogueUpgrade(UpgradeKind.RainbowAftershock, UpgradeFaction.Rainbow, UpgradeRarity.Rare, "彩虹余波", "彩球被手动触发后，在触发处留下一个炸弹。", 1, false),
                new RogueUpgrade(UpgradeKind.RainbowReserve, UpgradeFaction.Rainbow, UpgradeRarity.Epic, "彩虹储备", "每关开始时额外生成1/2个彩球。", 2, false),
                new RogueUpgrade(UpgradeKind.RainbowMutation, UpgradeFaction.Rainbow, UpgradeRarity.Epic, "彩虹异变", "彩球触发后，被消除的方块处有15%/30%/50%概率生成螺旋桨。", 3, false),

                new RogueUpgrade(UpgradeKind.PropellerCore, UpgradeFaction.Propeller, UpgradeRarity.Common, "螺旋核心", "单个螺旋桨额外锁定1个目标；每关开始生成4个螺旋桨。", 1, true),
                new RogueUpgrade(UpgradeKind.PropellerSpawn, UpgradeFaction.Propeller, UpgradeRarity.Common, "起飞补给", "累计触发8/6/4个特效后生成螺旋桨。", 3, false),
                new RogueUpgrade(UpgradeKind.PropellerReserve, UpgradeFaction.Propeller, UpgradeRarity.Common, "螺旋储备", "每关开始时额外生成4/6个螺旋桨。", 2, false),
                new RogueUpgrade(UpgradeKind.PropellerDamage, UpgradeFaction.Propeller, UpgradeRarity.Common, "螺旋扩容", "螺旋桨触发时，对障碍物的伤害+1/2。", 2, false),
                new RogueUpgrade(UpgradeKind.PropellerBoost, UpgradeFaction.Propeller, UpgradeRarity.Common, "螺旋增压", "螺旋桨原地爆炸时的范围扩大为横纵方向的2/3格。", 2, false),
                new RogueUpgrade(UpgradeKind.PropellerRebirth, UpgradeFaction.Propeller, UpgradeRarity.Epic, "螺旋重生", "每累计完成8次普通消除，生成1个螺旋桨。", 1, false),

                new RogueUpgrade(UpgradeKind.RemoveRed, UpgradeFaction.General, UpgradeRarity.Rare, "红色警告", "移除所有红色方块。", 1, false),
                new RogueUpgrade(UpgradeKind.RemoveBlue, UpgradeFaction.General, UpgradeRarity.Rare, "蓝色警告", "移除所有蓝色方块。", 1, false),
                new RogueUpgrade(UpgradeKind.RemoveYellow, UpgradeFaction.General, UpgradeRarity.Rare, "黄色警告", "移除所有黄色方块。", 1, false),
                new RogueUpgrade(UpgradeKind.RemoveOrange, UpgradeFaction.General, UpgradeRarity.Rare, "橙色警告", "移除所有橙色方块。", 1, false),
                new RogueUpgrade(UpgradeKind.RemovePurple, UpgradeFaction.General, UpgradeRarity.Rare, "紫色警告", "移除所有紫色方块。", 1, false),
                new RogueUpgrade(UpgradeKind.RemoveGreen, UpgradeFaction.General, UpgradeRarity.Rare, "绿色警告", "移除所有绿色方块。", 1, false),
                new RogueUpgrade(UpgradeKind.EdgeWalker, UpgradeFaction.General, UpgradeRarity.Common, "边缘行者", "每累计完成12/10/8次普通消除，对左右边缘列进行一次消除。", 3, false),
                new RogueUpgrade(UpgradeKind.BottomSweep, UpgradeFaction.General, UpgradeRarity.Common, "釜底抽薪", "每累计完成14/12/10次普通消除，对最底部的两行进行一次消除。", 3, false)
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

            foreach (var button in upgradeRefreshButtons)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private void FailRun()
        {
            RefreshAdButtons();
            ShowRunSummary(false, room);
        }

        private void ShowRunSummary(bool success, int reachedRoom)
        {
            inputLocked = true;
            SetUpgradePanel(false);
            ConfigureUpgradeTextForSummary();
            summaryPanel.gameObject.SetActive(true);
            summaryPanel.transform.SetAsLastSibling();
            upgradeText.transform.SetAsLastSibling();
            upgradeText.gameObject.SetActive(true);
            restartButton.gameObject.SetActive(true);
            restartButton.transform.SetAsLastSibling();
            restartButton.GetComponentInChildren<Text>().text = "再来一局";
            endlessButton.gameObject.SetActive(false);
            RefreshAdButtons();

            var completedRooms = success ? RoomsPerRun : Mathf.Max(0, reachedRoom - 1);
            var record = CreateRunRecord(success, reachedRoom, completedRooms);
            var leaderboard = SaveLeaderboardRecord(record);
            currentRunLeaderboardId = record.id;
            upgradeText.text =
                $"{(success ? "Run 完成" : $"Run 失败\n通关到第 {reachedRoom} 关")}\n" +
                $"本次分数：{record.score}\n" +
                $"{FormatLevelRemainingMoves(success, reachedRoom)}\n" +
                $"已选择技能：\n{FormatSkillNamesForSummary()}\n\n" +
                $"本局表现：\n{FormatRunStats(record)}\n\n" +
                $"本地排行榜：\n{FormatLeaderboard(leaderboard, record.id)}";
        }

        private void ConfigureUpgradeTextForChoices()
        {
            upgradeText.fontSize = 46;
            upgradeText.alignment = TextAnchor.MiddleCenter;
            upgradeText.rectTransform.sizeDelta = new Vector2(1040f, 150f);
            upgradeText.rectTransform.anchoredPosition = new Vector2(0f, -250f);
        }

        private void ConfigureUpgradeTextForSummary()
        {
            summaryPanel.rectTransform.sizeDelta = new Vector2(1040f, 1220f);
            summaryPanel.rectTransform.anchoredPosition = new Vector2(0f, -210f);
            upgradeText.fontSize = 24;
            upgradeText.alignment = TextAnchor.UpperCenter;
            upgradeText.rectTransform.sizeDelta = new Vector2(980f, 1120f);
            upgradeText.rectTransform.anchoredPosition = new Vector2(0f, -245f);
        }

        private void RefreshStatus()
        {
            targetScore = GetAdjustedTargetScore();
            statusText.text =
                $"Run  第 {room}/{RoomsPerRun} 关\n" +
                $"目标：{FormatTargetStatus()}\n" +
                $"分数 {score}\n" +
                $"剩余步数 {movesRemaining} / {roomMoveLimit}\n" +
                $"Run总分 {runScore} / 最高连锁 {Mathf.Max(1, bestComboChain)}\n" +
                $"已选强化：{FormatSkillNamesInline()}\n" +
                GetRoomGoalText();
            RefreshAdButtons();
        }

        private string FormatTargetStatus()
        {
            var targets = new List<string>();
            if (totalCrates > 0)
            {
                targets.Add($"木箱 {remainingCrates}/{totalCrates}");
            }

            if (totalIce > 0)
            {
                targets.Add($"冰块 {remainingIce}/{totalIce}");
            }

            return targets.Count == 0 ? "无" : string.Join("  ", targets);
        }

        private void RefreshAdButtons()
        {
            if (extraSkillAdButton == null || extraMovesAdButton == null || shuffleAdButton == null)
            {
                return;
            }

            var canUse = CanUseInLevelAd();
            var visible = !upgradePanelOpen && !AreLevelTargetsCleared();
            extraSkillAdButton.gameObject.SetActive(visible && !extraSkillAdUsedThisRun);
            extraSkillAdButton.interactable = canUse && !extraSkillAdUsedThisRun;
            extraMovesAdButton.gameObject.SetActive(visible && !extraMovesAdUsedThisLevel);
            extraMovesAdButton.interactable = canUse && !extraMovesAdUsedThisLevel;
            shuffleAdButton.gameObject.SetActive(visible && !shuffleAdUsedThisLevel);
            shuffleAdButton.interactable = canUse && !shuffleAdUsedThisLevel;
        }

        private RunLeaderboardRecord CreateRunRecord(bool success, int reachedRoom, int completedRooms)
        {
            var endTime = DateTime.Now;
            runScore = levelRemainingMoves.Sum();
            return new RunLeaderboardRecord
            {
                id = Environment.TickCount ^ endTime.GetHashCode(),
                score = runScore,
                timeTicks = endTime.Ticks,
                completed = success,
                reachedLevel = success ? RoomsPerRun : reachedRoom,
                durationSeconds = Mathf.Max(0, (int)(endTime - runStartTime).TotalSeconds),
                skillNames = activeUpgrades.Select(upgrade => upgrade.Name).ToArray(),
                levelRemainingMoves = levelRemainingMoves.Take(completedRooms).ToArray(),
                rocketTriggerCount = rocketActivationCount,
                bombTriggerCount = bombActivationCount,
                rainbowTriggerCount = rainbowActivationCount,
                propellerTriggerCount = propellerActivationCount,
                clearedBoxCount = clearedBoxCount,
                boxDamageTotal = boxDamageTotal,
                clearedIceCount = clearedIceCount,
                iceDamageTotal = iceDamageTotal,
                maxSingleClearCount = maxSingleClearCount,
                maxComboCount = bestComboChain,
                totalMovesUsed = totalMovesUsed,
                usedExtraSkillAd = extraSkillAdUsedThisRun,
                extraMovesAdUseCount = extraMovesAdUseCount,
                shuffleAdUseCount = shuffleAdUseCount,
                skillRefreshAdUseCount = skillRefreshAdUseCount
            };
        }

        private List<RunLeaderboardRecord> SaveLeaderboardRecord(RunLeaderboardRecord record)
        {
            var leaderboard = LoadLeaderboard();
            leaderboard.Add(record);
            leaderboard = leaderboard
                .OrderByDescending(entry => entry.score)
                .ThenByDescending(entry => entry.completed)
                .ThenByDescending(entry => entry.timeTicks)
                .Take(10)
                .ToList();
            for (var i = 0; i < leaderboard.Count; i++)
            {
                leaderboard[i].rank = i + 1;
            }

            PlayerPrefs.SetString(LeaderboardPrefsKey, JsonUtility.ToJson(new RunLeaderboardData { records = leaderboard.ToArray() }));
            PlayerPrefs.Save();
            return leaderboard;
        }

        private List<RunLeaderboardRecord> LoadLeaderboard()
        {
            var json = PlayerPrefs.GetString(LeaderboardPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new List<RunLeaderboardRecord>();
            }

            try
            {
                var data = JsonUtility.FromJson<RunLeaderboardData>(json);
                return data?.records?.ToList() ?? new List<RunLeaderboardRecord>();
            }
            catch (Exception)
            {
                return new List<RunLeaderboardRecord>();
            }
        }

        private string FormatLevelRemainingMoves(bool success, int reachedRoom)
        {
            var lines = new List<string> { "每关剩余步数：" };
            for (var i = 0; i < levelRemainingMoves.Count; i++)
            {
                lines.Add($"第{i + 1}关：剩余{levelRemainingMoves[i]}步");
            }

            if (!success)
            {
                lines.Add($"第{reachedRoom}关：失败");
            }

            return string.Join("\n", lines);
        }

        private string FormatSkillNamesForSummary()
        {
            if (activeUpgrades.Count == 0)
            {
                return "- 无";
            }

            return string.Join("\n", activeUpgrades.Select(upgrade => $"- {upgrade.Name}"));
        }

        private string FormatSkillNamesInline()
        {
            return activeUpgrades.Count == 0 ? "无" : string.Join("、", activeUpgrades.Select(upgrade => upgrade.Name));
        }

        private string FormatRunStats(RunLeaderboardRecord record)
        {
            var lines = new List<string>
            {
                $"- 触发火箭：{record.rocketTriggerCount}次",
                $"- 触发炸弹：{record.bombTriggerCount}次",
                $"- 触发彩虹球：{record.rainbowTriggerCount}次",
                $"- 触发螺旋桨：{record.propellerTriggerCount}次",
                $"- 清除木箱：{record.clearedBoxCount}个",
                $"- 木箱总伤害：{record.boxDamageTotal}",
                $"- 清除冰块：{record.clearedIceCount}个",
                $"- 冰块总伤害：{record.iceDamageTotal}",
                $"- 最大单次清除：{record.maxSingleClearCount}",
                $"- 最高连锁：{record.maxComboCount}",
                $"- 使用步数：{record.totalMovesUsed}"
            };
            return string.Join("\n", lines);
        }

        private string FormatLeaderboard(List<RunLeaderboardRecord> leaderboard, int currentRecordId)
        {
            if (leaderboard.Count == 0)
            {
                return "暂无记录";
            }

            return string.Join("\n", leaderboard.Select((entry, index) =>
            {
                var marker = entry.id == currentRecordId ? "（本次）" : string.Empty;
                return $"第{index + 1}名｜{entry.score}分｜{FormatLeaderboardTime(entry.timeTicks)}{marker}";
            }));
        }

        private string FormatLeaderboardTime(long ticks)
        {
            return new DateTime(ticks).ToString("yyyy-MM-dd HH:mm");
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
                    return "第1关：启动关，清掉少量木箱并确认Build方向。";
                case 2:
                    return "第2关：Build启动关，木箱压力略微提升。";
                case 3:
                    return "第3关：Build强化关，开始处理更多双层木箱。";
                case 4:
                    return "第4关：分叉/成型关，利用技能加速破局。";
                case 5:
                    return "第5关：爽感预演关，给连锁和特效留出空间。";
                default:
                    return "第6关：高潮关，清空最高压力木箱并打出Build表现。";
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

            RefreshAllIceOverlays();

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

        private bool IsMovableTile(Tile tile)
        {
            return tile != null && tile.CrateHealth <= 0;
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
                : this(levelIndex, $"Level {levelIndex:00}", boardWidth, boardHeight, moveLimit, TileTypes, ConvertLegacyBoxRows(boxMapRows))
            {
            }

            public LevelConfig(int levelIndex, string levelName, int boardWidth, int boardHeight, int moveLimit, int colorCount, string[] gridRows)
            {
                LevelIndex = levelIndex;
                LevelName = levelName;
                BoardWidth = boardWidth;
                BoardHeight = boardHeight;
                MoveLimit = moveLimit;
                ColorCount = colorCount;
                GridRows = gridRows ?? Array.Empty<string>();
            }

            public int LevelIndex { get; }
            public string LevelName { get; }
            public int BoardWidth { get; }
            public int BoardHeight { get; }
            public int MoveLimit { get; }
            public int ColorCount { get; }
            public string[] GridRows { get; }

            private static string[] ConvertLegacyBoxRows(string[] rows)
            {
                return (rows ?? Array.Empty<string>())
                    .Select(row => string.Join(",", (row ?? string.Empty).Select(cell => cell == '.' ? "." : $"B{cell}")))
                    .ToArray();
            }
        }

        private struct LevelCell
        {
            public static readonly LevelCell Empty = new LevelCell(0, 0);

            public LevelCell(int crateHealth, int iceHealth)
            {
                CrateHealth = crateHealth;
                IceHealth = iceHealth;
            }

            public int CrateHealth { get; }
            public int IceHealth { get; }
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

        [Serializable]
        private sealed class RunLeaderboardData
        {
            public RunLeaderboardRecord[] records = Array.Empty<RunLeaderboardRecord>();
        }

        [Serializable]
        private sealed class RunLeaderboardRecord
        {
            public int id;
            public int rank;
            public int score;
            public long timeTicks;
            public bool completed;
            public int reachedLevel;
            public int durationSeconds;
            public string[] skillNames = Array.Empty<string>();
            public int[] levelRemainingMoves = Array.Empty<int>();
            public int rocketTriggerCount;
            public int bombTriggerCount;
            public int rainbowTriggerCount;
            public int propellerTriggerCount;
            public int clearedBoxCount;
            public int boxDamageTotal;
            public int clearedIceCount;
            public int iceDamageTotal;
            public int maxSingleClearCount;
            public int maxComboCount;
            public int totalMovesUsed;
            public bool usedExtraSkillAd;
            public int extraMovesAdUseCount;
            public int shuffleAdUseCount;
            public int skillRefreshAdUseCount;
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
            BombDamage,
            BombSpawn,
            BombReserve,
            ExplosionAftershock,
            RocketCore,
            RocketReserve,
            RocketDamage,
            RocketAftershock,
            RocketSplit,
            RocketSpawn,
            RainbowCore,
            RainbowSpawn,
            RainbowAftershock,
            RainbowReserve,
            RainbowMutation,
            PropellerCore,
            PropellerSpawn,
            PropellerReserve,
            PropellerDamage,
            PropellerBoost,
            PropellerRebirth,
            RemoveRed,
            RemoveBlue,
            RemoveYellow,
            RemoveOrange,
            RemovePurple,
            RemoveGreen,
            EdgeWalker,
            BottomSweep
        }

        private enum UpgradeFaction
        {
            Explosion,
            Rocket,
            Rainbow,
            Propeller,
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
