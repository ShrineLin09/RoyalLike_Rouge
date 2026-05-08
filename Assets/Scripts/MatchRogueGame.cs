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
        private const int Width = 8;
        private const int Height = 8;
        private const int TileTypes = 6;
        private const float MatchPauseSeconds = 0.07f;
        private const float ClearAnimSeconds = 0.18f;
        private const float FallAnimSecondsPerCell = 0.055f;
        private const float MaxFallAnimSeconds = 0.26f;
        private const float CascadePauseSeconds = 0.08f;

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
        private float lastClickTime;

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

            SetTileBaseColor(tile);
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
            return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y) == 1;
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

        private void TrySwap(Vector2Int a, Vector2Int b)
        {
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
                AddTilesOfType(GetMostCommonTileType(), clearSet);
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
            if (board[specialPos.x, specialPos.y].Special == SpecialKind.Rainbow)
            {
                AddTilesOfType(board[normalPos.x, normalPos.y].Type, clearSet);
                ApplyRainbowBonusClears(specialPos, clearSet);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
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

            if (firstIsRocket && secondIsRocket)
            {
                AddRow(a.y, clearSet);
                AddColumn(a.x, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            if (firstSpecial == SpecialKind.Bomb && secondSpecial == SpecialKind.Bomb)
            {
                AddRadius(a, 2, clearSet);
                AddRadius(b, 2, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            if ((firstIsRocket && secondSpecial == SpecialKind.Bomb) || (secondIsRocket && firstSpecial == SpecialKind.Bomb))
            {
                AddWideCross(a, clearSet);
                AwardScoreForClears(clearSet.Count);
                ResolveClearSet(clearSet, null, false);
                return;
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
        }

        private void ResolveRainbowSpecialCombination(SpecialKind targetSpecial, HashSet<Vector2Int> clearSet)
        {
            var targetType = GetMostCommonTileType();
            var positions = GetTilesOfType(targetType);
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
                clearSet.Add(pos);
            }

            AwardScoreForClears(clearSet.Count);
            ResolveClearSet(clearSet, null);
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

        private void AddTilesOfType(int targetType, HashSet<Vector2Int> output)
        {
            foreach (var pos in GetTilesOfType(targetType))
            {
                output.Add(pos);
            }
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
            scoreGain += comboChain > 1 ? comboChain * 40 : 0;
            score += scoreGain;
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

                yield return AnimateAndRemoveClears(bonusClears);

                if (currentSpecial.HasValue)
                {
                    CreateSpecialTileAt(currentSpecial.Value);
                }

                ApplyPostClearUpgradeSpawns(bonusClears);

                var fallMoves = ApplyGravity();
                var spawnMoves = RefillBoard();
                fallMoves.AddRange(spawnMoves);
                yield return AnimateFalls(fallMoves);
                yield return new WaitForSeconds(CascadePauseSeconds);

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

            if (score >= targetScore)
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

        private void AddNeighbors(Vector2Int center, HashSet<Vector2Int> output)
        {
            AddRadius(center, 1, output);
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
            TryCreateRandomSpecial(UpgradeKind.BombSpawn, SpecialKind.Bomb, clearedPositions.Count, 0.18f, 0.32f, 0.48f);
            TryCreateRandomSpecial(UpgradeKind.RocketSpawn, rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical, clearedPositions.Count, 0.16f, 0.30f, 0.45f);
            TryCreateRandomSpecial(UpgradeKind.RocketOnHit, rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical, clearedPositions.Count, 0.10f, 0.22f, 0.22f);
            TryCreateRandomSpecial(UpgradeKind.RainbowAfterSpecial, RollRandomSpecial(), clearedPositions.Count, 0.14f, 0.28f, 0.42f);
            TryCreateRandomSpecial(UpgradeKind.RainbowCopy, SpecialKind.Rainbow, clearedPositions.Count, 0.10f, 0.20f, 0.20f);
        }

        private void TryCreateRandomSpecial(UpgradeKind kind, SpecialKind special, int clearedCount, float levelOneChance, float levelTwoChance, float levelThreeChance)
        {
            var level = GetUpgradeLevel(kind);
            if (level <= 0 || clearedCount <= 0)
            {
                return;
            }

            var chance = GetChanceByLevel(level, levelOneChance, levelTwoChance, levelThreeChance);
            if (UnityEngine.Random.value > chance)
            {
                return;
            }

            var spawnCount = clearedCount >= 18 && level >= 2 ? 2 : 1;
            for (var i = 0; i < spawnCount; i++)
            {
                CreateSpecialOnRandomColorTile(special);
            }
        }

        private float GetChanceByLevel(int level, float levelOne, float levelTwo, float levelThree)
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

        private SpecialKind RollRandomSpecial()
        {
            var roll = rng.Next(4);
            if (roll == 0)
            {
                return SpecialKind.Bomb;
            }

            if (roll == 1)
            {
                return SpecialKind.Rainbow;
            }

            return rng.Next(2) == 0 ? SpecialKind.LineHorizontal : SpecialKind.LineVertical;
        }

        private void CreateSpecialOnRandomColorTile(SpecialKind special)
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

            if (candidates.Count == 0)
            {
                return;
            }

            var pos = candidates[rng.Next(candidates.Count)];
            board[pos.x, pos.y].Special = special;
            DecorateTile(board[pos.x, pos.y]);
        }

        private void ExpandSpecialClears(HashSet<Vector2Int> baseClears, HashSet<Vector2Int> output)
        {
            var expandedSpecials = new HashSet<Vector2Int>();
            var queue = baseClears.Where(IsSpecialTile).ToList();
            var index = 0;

            while (index < queue.Count)
            {
                var pos = queue[index++];
                if (!IsInside(pos) || board[pos.x, pos.y] == null)
                {
                    continue;
                }

                if (expandedSpecials.Contains(pos))
                {
                    continue;
                }

                expandedSpecials.Add(pos);
                var before = output.Count;
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
                        AddTilesOfType(GetMostCommonTileType(), output);
                        ApplyRainbowBonusClears(pos, output);
                        break;
                }

                if (output.Count != before)
                {
                    queue = output.Where(IsSpecialTile).ToList();
                }
            }
        }

        private void AddRocketClear(Vector2Int pos, MatchOrientation orientation, HashSet<Vector2Int> output)
        {
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
            if (extraLevel > 0 && UnityEngine.Random.value < GetChanceByLevel(extraLevel, 0.35f, 0.65f, 1f))
            {
                if (orientation == MatchOrientation.Horizontal)
                {
                    AddRow(Mathf.Clamp(pos.y + (rng.Next(2) == 0 ? -1 : 1), 0, Height - 1), output);
                }
                else
                {
                    AddColumn(Mathf.Clamp(pos.x + (rng.Next(2) == 0 ? -1 : 1), 0, Width - 1), output);
                }
            }
        }

        private void AddBombClear(Vector2Int pos, HashSet<Vector2Int> output)
        {
            var radius = 1 + Mathf.Min(2, GetUpgradeLevel(UpgradeKind.BombRadius));
            if (GetUpgradeLevel(UpgradeKind.ExplosionAftershock) > 0)
            {
                radius += 1;
            }

            AddRadius(pos, radius, output);
        }

        private void ApplyRainbowBonusClears(Vector2Int pos, HashSet<Vector2Int> output)
        {
            if (GetUpgradeLevel(UpgradeKind.RainbowChainBomb) > 0)
            {
                AddRadius(pos, 1 + GetUpgradeLevel(UpgradeKind.RainbowChainBomb), output);
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
                    ? SpecialKind.LineVertical
                    : SpecialKind.LineHorizontal;
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

        private void SetTileBaseColor(Tile tile)
        {
            var renderer = tile.Object.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.material.color = tile.Special == SpecialKind.None
                ? tileColors[tile.Type]
                : new Color(0.92f, 0.88f, 0.72f);
        }

        private Color GetTileDisplayColor(Tile tile)
        {
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
                        board[x, y] = CreateTile(x, y, rng.Next(TileTypes));
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

            SetUpgradePanel(true);
            var choices = RollUpgradeChoices();
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
                button.GetComponentInChildren<Text>().text = $"{GetFactionLabel(upgrade.Faction)} {upgrade.Name}\n{upgrade.Description}";
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    activeUpgrades.Add(upgrade);
                    StartRoom();
                });
            }
        }

        private RogueUpgrade[] RollUpgradeChoices()
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
            if (upgrade.IsCore)
            {
                return HasAnyCore() ? 18f : 70f;
            }

            var factionLevel = GetFactionInvestment(upgrade.Faction);
            return 45f + factionLevel * 18f;
        }

        private bool IsUpgradeUnlocked(RogueUpgrade upgrade)
        {
            return upgrade.IsCore || HasFactionCore(upgrade.Faction);
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

        private int GetUpgradeLevel(UpgradeKind kind)
        {
            return activeUpgrades.Count(upgrade => upgrade.Kind == kind);
        }

        private RogueUpgrade[] GetUpgradePool()
        {
            return new[]
            {
                new RogueUpgrade(UpgradeKind.ExplosionCore, UpgradeFaction.Explosion, "爆破核心", "解锁爆炸流。炸弹触发会尝试制造更多爆点。", 1, true),
                new RogueUpgrade(UpgradeKind.BombRadius, UpgradeFaction.Explosion, "炸弹扩容", "炸弹爆炸范围提升。", 3, false),
                new RogueUpgrade(UpgradeKind.BombChain, UpgradeFaction.Explosion, "连锁引爆", "炸弹会引爆范围内的其他特效。", 1, false),
                new RogueUpgrade(UpgradeKind.BombSpawn, UpgradeFaction.Explosion, "越炸越多", "每轮爆炸后有概率把普通棋子变成炸弹。", 3, false),
                new RogueUpgrade(UpgradeKind.ExplosionAftershock, UpgradeFaction.Explosion, "爆炸余波", "炸弹额外清除周围棋子。", 2, false),

                new RogueUpgrade(UpgradeKind.RocketCore, UpgradeFaction.Rocket, "火箭核心", "解锁火箭流。火箭更容易连续扫屏。", 1, true),
                new RogueUpgrade(UpgradeKind.RocketSplit, UpgradeFaction.Rocket, "火箭分裂", "火箭触发时额外扫过交叉方向。", 2, false),
                new RogueUpgrade(UpgradeKind.RocketExtra, UpgradeFaction.Rocket, "额外发射", "火箭有概率额外发射邻近轨道。", 3, false),
                new RogueUpgrade(UpgradeKind.RocketSpawn, UpgradeFaction.Rocket, "火箭补给", "每轮清除后有概率生成火箭。", 3, false),
                new RogueUpgrade(UpgradeKind.RocketOnHit, UpgradeFaction.Rocket, "扫屏回响", "火箭命中后更容易留下新火箭。", 2, false),

                new RogueUpgrade(UpgradeKind.RainbowCore, UpgradeFaction.Rainbow, "彩虹核心", "解锁彩虹流。彩球会制造更多失控连锁。", 1, true),
                new RogueUpgrade(UpgradeKind.RainbowCopy, UpgradeFaction.Rainbow, "彩虹复制", "彩球触发后有概率复制新的彩球。", 2, false),
                new RogueUpgrade(UpgradeKind.RainbowAfterSpecial, UpgradeFaction.Rainbow, "彩虹裂变", "彩球触发后生成随机特效。", 3, false),
                new RogueUpgrade(UpgradeKind.RainbowChainBomb, UpgradeFaction.Rainbow, "彩爆连锁", "彩球触发时额外制造炸弹爆点。", 2, false)
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
                $"已选强化：{FormatActiveUpgrades()}\n" +
                "交换相邻棋子，在步数耗尽前达到目标分数。";
        }

        private string FormatActiveUpgrades()
        {
            if (activeUpgrades.Count == 0)
            {
                return "无";
            }

            return string.Join("、", activeUpgrades
                .GroupBy(upgrade => upgrade.Name)
                .Select(group => group.Count() > 1 ? $"{group.Key} Lv.{group.Count()}" : group.Key));
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
                default:
                    return "[通用]";
            }
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
            return tile != null && tile.Special == SpecialKind.None;
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

        private struct RogueUpgrade
        {
            public RogueUpgrade(UpgradeKind kind, UpgradeFaction faction, string name, string description, int maxLevel, bool isCore)
            {
                Kind = kind;
                Faction = faction;
                Name = name;
                Description = description;
                MaxLevel = maxLevel;
                IsCore = isCore;
            }

            public UpgradeKind Kind { get; }
            public UpgradeFaction Faction { get; }
            public string Name { get; }
            public string Description { get; }
            public int MaxLevel { get; }
            public bool IsCore { get; }
        }

        private enum UpgradeKind
        {
            ExplosionCore,
            BombRadius,
            BombChain,
            BombSpawn,
            ExplosionAftershock,
            RocketCore,
            RocketSplit,
            RocketExtra,
            RocketSpawn,
            RocketOnHit,
            RainbowCore,
            RainbowCopy,
            RainbowAfterSpecial,
            RainbowChainBomb
        }

        private enum UpgradeFaction
        {
            Explosion,
            Rocket,
            Rainbow
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
