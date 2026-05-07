using System;
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
        private const int Width = 8;
        private const int Height = 8;
        private const int TileTypes = 6;

        private readonly Color[] tileColors =
        {
            new Color(0.95f, 0.22f, 0.26f),
            new Color(0.20f, 0.55f, 1.00f),
            new Color(0.21f, 0.78f, 0.36f),
            new Color(1.00f, 0.78f, 0.12f),
            new Color(0.67f, 0.33f, 0.95f),
            new Color(1.00f, 0.48f, 0.16f)
        };

        private readonly Tile[,] board = new Tile[Width, Height];
        private readonly List<RogueUpgrade> activeUpgrades = new List<RogueUpgrade>();
        private readonly System.Random rng = new System.Random();

        private Camera mainCamera;
        private Transform boardRoot;
        private Canvas canvas;
        private Text statusText;
        private Text upgradeText;
        private RectTransform statusRect;
        private Button[] upgradeButtons;
        private Button restartButton;
        private Button endlessButton;
        private Texture2D lineHorizontalIcon;
        private Texture2D lineVerticalIcon;
        private Texture2D bombIcon;
        private Texture2D rainbowIcon;

        private Vector2Int? selected;
        private bool inputLocked;
        private bool upgradePanelOpen;
        private bool isEndless;
        private int layer = 1;
        private int room = 1;
        private int score;
        private int baseTargetScore;
        private int targetScore;
        private int movesRemaining;
        private int roomMoveLimit;
        private int comboChain;
        private float tileSpacing = 1f;
        private float tileScale = 0.82f;
        private Vector3 boardOrigin;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Awake()
        {
            BuildScene();
            StartRun(false);
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
                return;
            }

            if (TryGetPrimaryPressPosition(out var screenPosition))
            {
                TrySelectTile(screenPosition);
            }

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
        }

        private void ConfigureCameraAndBoardLayout()
        {
            lastScreenWidth = Mathf.Max(1, Screen.width);
            lastScreenHeight = Mathf.Max(1, Screen.height);

            var aspect = lastScreenWidth / (float)lastScreenHeight;
            tileSpacing = 1f;
            tileScale = 0.82f;

            var boardWidth = (Width - 1) * tileSpacing + tileScale;
            var boardHeight = (Height - 1) * tileSpacing + tileScale;
            var requiredHalfHeightForWidth = boardWidth / (2f * Mathf.Max(0.1f, aspect)) + 0.28f;
            var requiredHalfHeightForHeight = boardHeight * 0.5f + 1.95f;
            mainCamera.orthographicSize = Mathf.Max(6.4f, requiredHalfHeightForWidth, requiredHalfHeightForHeight);

            var boardCenter = new Vector3(0f, -0.35f, 0f);
            boardOrigin = boardCenter - new Vector3((Width - 1) * tileSpacing * 0.5f, (Height - 1) * tileSpacing * 0.5f, 0f);
        }

        private void BuildBackground()
        {
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

        private void BuildUi()
        {
            var canvasObject = new GameObject("Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080f, 1920f);
            canvasObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            statusText = CreateText("Status", new Vector2(32f, -132f), new Vector2(760f, 250f), 28, TextAnchor.UpperLeft);
            statusRect = statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            upgradeText = CreateText("UpgradeTitle", new Vector2(0f, -430f), new Vector2(1000f, 120f), 40, TextAnchor.MiddleCenter);
            upgradeText.text = "";

            upgradeButtons = new Button[3];
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                upgradeButtons[i] = CreateButton($"Upgrade {i + 1}", new Vector2(0f, 120f - i * 140f), new Vector2(860f, 108f));
            }

            restartButton = CreateButton("Restart", new Vector2(-230f, -790f), new Vector2(360f, 90f));
            restartButton.GetComponentInChildren<Text>().text = "重新开始";
            restartButton.onClick.AddListener(() => StartRun(isEndless));

            endlessButton = CreateButton("Endless", new Vector2(230f, -790f), new Vector2(360f, 90f));
            endlessButton.GetComponentInChildren<Text>().text = "开始无尽";
            endlessButton.onClick.AddListener(() => StartRun(true));

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

        private void StartRun(bool endless)
        {
            isEndless = endless;
            layer = 1;
            room = 1;
            activeUpgrades.Clear();
            selected = null;
            inputLocked = false;
            endlessButton.GetComponentInChildren<Text>().text = isEndless ? "无尽模式" : "开始无尽";
            GenerateBoard();
            StartRoom();
        }

        private void StartRoom()
        {
            inputLocked = false;
            selected = null;
            RefreshBoardTransforms();
            score = 0;
            comboChain = 0;
            roomMoveLimit = Mathf.Max(14, 24 - (layer - 1) - Mathf.FloorToInt((room - 1) * 0.5f));
            roomMoveLimit += Mathf.RoundToInt(GetUpgradeValue(UpgradeKind.ExtraMoves));
            movesRemaining = roomMoveLimit;
            baseTargetScore = Mathf.RoundToInt((950 + room * 170 + layer * 260) * GetDifficultyMultiplier());
            targetScore = GetAdjustedTargetScore();
            SetUpgradePanel(false);
            RefreshStatus();
        }

        private float GetDifficultyMultiplier()
        {
            return 1f + (layer - 1) * 0.18f + (room - 1) * 0.06f;
        }

        private void GenerateBoard()
        {
            ClearTiles();

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var type = RollTileTypeAvoidingMatch(x, y);
                    board[x, y] = CreateTile(x, y, type);
                }
            }
        }

        private int RollTileTypeAvoidingMatch(int x, int y)
        {
            for (var attempts = 0; attempts < 20; attempts++)
            {
                var type = rng.Next(TileTypes);
                var horizontal = x >= 2 && board[x - 1, y] != null && board[x - 2, y] != null &&
                                 board[x - 1, y].Type == type && board[x - 2, y].Type == type;
                var vertical = y >= 2 && board[x, y - 1] != null && board[x, y - 2] != null &&
                               board[x, y - 1].Type == type && board[x, y - 2].Type == type;
                if (!horizontal && !vertical)
                {
                    return type;
                }
            }

            return rng.Next(TileTypes);
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
            mark.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
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

            if (!selected.HasValue)
            {
                Select(grid);
                return;
            }

            var from = selected.Value;
            if (from == grid)
            {
                Deselect(from);
                selected = null;
                return;
            }

            if (Mathf.Abs(from.x - grid.x) + Mathf.Abs(from.y - grid.y) == 1)
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

        private void Select(Vector2Int grid)
        {
            selected = grid;
            board[grid.x, grid.y].Object.transform.localScale = Vector3.one * (tileScale + 0.16f);
        }

        private void Deselect(Vector2Int grid)
        {
            if (IsInside(grid) && board[grid.x, grid.y] != null)
            {
                board[grid.x, grid.y].Object.transform.localScale = Vector3.one * tileScale;
            }
        }

        private void TrySwap(Vector2Int a, Vector2Int b)
        {
            SwapTiles(a, b);
            if (TryResolveRainbowSwap(a, b))
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

        private void SwapTiles(Vector2Int a, Vector2Int b)
        {
            (board[a.x, a.y], board[b.x, b.y]) = (board[b.x, b.y], board[a.x, a.y]);
            board[a.x, a.y].Object.transform.position = GridToWorld(a.x, a.y);
            board[b.x, b.y].Object.transform.position = GridToWorld(b.x, b.y);
        }

        private bool TryResolveRainbowSwap(Vector2Int a, Vector2Int b)
        {
            var first = board[a.x, a.y];
            var second = board[b.x, b.y];
            if (first == null || second == null)
            {
                return false;
            }

            if (first.Special != SpecialKind.Rainbow && second.Special != SpecialKind.Rainbow)
            {
                return false;
            }

            var targetType = first.Special == SpecialKind.Rainbow ? second.Type : first.Type;
            var clearSet = new HashSet<Vector2Int>();
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y] != null && board[x, y].Type == targetType)
                    {
                        clearSet.Add(new Vector2Int(x, y));
                    }
                }
            }

            clearSet.Add(a);
            clearSet.Add(b);
            inputLocked = true;
            comboChain++;
            movesRemaining = Mathf.Max(0, movesRemaining - 1);
            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
            return true;
        }

        private void ResolveMatches(List<MatchGroup> matchGroups, Vector2Int? specialSpawn)
        {
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
            scoreGain += Mathf.RoundToInt(scoreGain * GetUpgradeValue(UpgradeKind.ScorePercent) / 100f);
            scoreGain += comboChain > 1 ? comboChain * 40 : 0;
            score += scoreGain;
        }

        private void ResolveClearSet(HashSet<Vector2Int> baseClears, PendingSpecial? specialToCreate)
        {
            var bombChance = GetUpgradeValue(UpgradeKind.BombChance);
            var bonusClears = new HashSet<Vector2Int>(baseClears);
            ExpandSpecialClears(baseClears, bonusClears);
            foreach (var pos in baseClears.ToArray())
            {
                if (UnityEngine.Random.value * 100f < bombChance)
                {
                    AddNeighbors(pos, bonusClears);
                }
            }

            if (specialToCreate.HasValue)
            {
                bonusClears.Remove(specialToCreate.Value.Position);
            }

            foreach (var pos in bonusClears)
            {
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                Destroy(board[pos.x, pos.y].Object);
                board[pos.x, pos.y] = null;
            }

            if (specialToCreate.HasValue)
            {
                CreateSpecialTileAt(specialToCreate.Value);
            }

            ApplyGravity();
            RefillBoard();

            var cascades = FindMatchGroups();
            if (cascades.Count > 0)
            {
                ResolveMatches(cascades, GetPreferredSpecialSpawn(null, null, cascades));
                return;
            }

            comboChain = 0;
            inputLocked = false;

            if (score >= targetScore)
            {
                CompleteRoom();
                return;
            }

            if (movesRemaining <= 0)
            {
                FailRun();
            }
        }

        private void AddNeighbors(Vector2Int center, HashSet<Vector2Int> output)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    output.Add(new Vector2Int(center.x + dx, center.y + dy));
                }
            }
        }

        private void ExpandSpecialClears(HashSet<Vector2Int> baseClears, HashSet<Vector2Int> output)
        {
            foreach (var pos in baseClears.ToArray())
            {
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                var tile = board[pos.x, pos.y];
                switch (tile.Special)
                {
                    case SpecialKind.LineHorizontal:
                        for (var x = 0; x < Width; x++)
                        {
                            output.Add(new Vector2Int(x, pos.y));
                        }
                        break;
                    case SpecialKind.LineVertical:
                        for (var y = 0; y < Height; y++)
                        {
                            output.Add(new Vector2Int(pos.x, y));
                        }
                        break;
                    case SpecialKind.Bomb:
                        AddNeighbors(pos, output);
                        break;
                    case SpecialKind.Rainbow:
                        var targetType = tile.Type;
                        for (var x = 0; x < Width; x++)
                        {
                            for (var y = 0; y < Height; y++)
                            {
                                if (board[x, y] != null && board[x, y].Type == targetType)
                                {
                                    output.Add(new Vector2Int(x, y));
                                }
                            }
                        }
                        break;
                }
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
                    var same = x < Width && board[x, y] != null && board[runStart, y] != null &&
                               board[x, y].Type == board[runStart, y].Type;
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
                    var same = y < Height && board[x, y] != null && board[x, runStart] != null &&
                               board[x, y].Type == board[x, runStart].Type;
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
                    ? SpecialKind.LineHorizontal
                    : SpecialKind.LineVertical;
                return new PendingSpecial(position, specialKind);
            }

            return null;
        }

        private void CreateSpecialTileAt(PendingSpecial pendingSpecial)
        {
            var pos = pendingSpecial.Position;
            if (!IsInside(pos) || board[pos.x, pos.y] == null)
            {
                return;
            }

            board[pos.x, pos.y].Special = pendingSpecial.Special;
            DecorateTile(board[pos.x, pos.y]);
        }

        private void ApplyGravity()
        {
            for (var x = 0; x < Width; x++)
            {
                var writeY = 0;
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y] == null)
                    {
                        continue;
                    }

                    if (writeY != y)
                    {
                        board[x, writeY] = board[x, y];
                        board[x, y] = null;
                        board[x, writeY].Object.transform.position = GridToWorld(x, writeY);
                    }

                    writeY++;
                }
            }
        }

        private void RefillBoard()
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (board[x, y] == null)
                    {
                        board[x, y] = CreateTile(x, y, rng.Next(TileTypes));
                    }
                }
            }
        }

        private void CompleteRoom()
        {
            inputLocked = true;
            if (room >= 5)
            {
                layer++;
                room = 1;
            }
            else
            {
                room++;
            }

            ShowUpgradeChoices();
        }

        private void ShowUpgradeChoices()
        {
            upgradeText.text = isEndless
                ? $"第 {layer} 层奖励"
                : room == 1 ? "进入下一大关" : $"第 {room - 1} 小关完成";

            var choices = RollUpgradeChoices();
            for (var i = 0; i < upgradeButtons.Length; i++)
            {
                var upgrade = choices[i];
                var button = upgradeButtons[i];
                button.GetComponentInChildren<Text>().text = $"{upgrade.Name}\n{upgrade.Description}";
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    activeUpgrades.Add(upgrade);
                    StartRoom();
                });
            }

            SetUpgradePanel(true);
        }

        private RogueUpgrade[] RollUpgradeChoices()
        {
            var pool = new[]
            {
                new RogueUpgrade(UpgradeKind.ScorePercent, "连消狂热", "消除得分 +20%。", 20f),
                new RogueUpgrade(UpgradeKind.BombChance, "火花棋子", "被消除的棋子有 8% 概率清除周围格子。", 8f),
                new RogueUpgrade(UpgradeKind.ExtraMoves, "从容开局", "每个小关初始步数 +3。", 3f),
                new RogueUpgrade(UpgradeKind.TargetDiscount, "目标减压", "后续小关目标分数 -8%。", 8f),
                new RogueUpgrade(UpgradeKind.ScorePercent, "巨型连锁", "消除得分 +35%。", 35f),
                new RogueUpgrade(UpgradeKind.BombChance, "炽热棋盘", "被消除的棋子有 14% 概率清除周围格子。", 14f)
            };

            return pool.OrderBy(_ => rng.Next()).Take(3).ToArray();
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

        private float GetUpgradeValue(UpgradeKind kind)
        {
            var total = activeUpgrades.Where(upgrade => upgrade.Kind == kind).Sum(upgrade => upgrade.Value);
            if (kind == UpgradeKind.TargetDiscount)
            {
                return Mathf.Clamp(total, 0f, 50f);
            }

            return total;
        }

        private void FailRun()
        {
            inputLocked = true;
            SetUpgradePanel(false);
            upgradeText.gameObject.SetActive(true);
            upgradeText.text = isEndless
                ? $"挑战结束：第 {layer} 层，第 {room} 小关"
                : "小关失败，再试一次。";
        }

        private void RefreshStatus()
        {
            targetScore = GetAdjustedTargetScore();
            statusText.text =
                $"{(isEndless ? "无尽挑战" : "原型闯关")}  第 {layer} 层 / 第 {room}/5 小关\n" +
                $"分数 {score} / {targetScore}\n" +
                $"剩余步数 {movesRemaining} / {roomMoveLimit}\n" +
                $"已选强化：{(activeUpgrades.Count == 0 ? "无" : string.Join("、", activeUpgrades.Select(u => u.Name).Distinct()))}\n" +
                "交换相邻棋子，在步数耗尽前达到目标分数。";
        }

        private bool IsPointerOverUi()
        {
            return upgradePanelOpen || EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private int GetAdjustedTargetScore()
        {
            var discount = GetUpgradeValue(UpgradeKind.TargetDiscount);
            return Mathf.Max(300, Mathf.RoundToInt(baseTargetScore * (1f - discount / 100f)));
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
            public GameObject Object { get; }
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

        private struct RogueUpgrade
        {
            public RogueUpgrade(UpgradeKind kind, string name, string description, float value)
            {
                Kind = kind;
                Name = name;
                Description = description;
                Value = value;
            }

            public UpgradeKind Kind { get; }
            public string Name { get; }
            public string Description { get; }
            public float Value { get; }
        }

        private enum UpgradeKind
        {
            ScorePercent,
            BombChance,
            ExtraMoves,
            TargetDiscount
        }

        private enum MatchOrientation
        {
            Horizontal,
            Vertical
        }

        private enum SpecialKind
        {
            None,
            LineHorizontal,
            LineVertical,
            Bomb,
            Rainbow
        }
    }
}
