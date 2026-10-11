using System;
using System.Collections.Generic;
using KSP.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaveTimelines
{
    /// <summary>
    /// Draws the tree onto the load dialog's rows and handles collapsing.
    ///
    /// Every nested group has a line down the left of its rows, in a column
    /// of its own, colored by depth. The line starts with a cap near the top
    /// of the group's first row and ends with a cap near the bottom of its
    /// last, and runs unbroken past the rows of any groups nested inside it,
    /// so a deep row has one line for each group around it. The top level
    /// has no line.
    ///
    /// Clicking a line collapses its group to its first row, which then
    /// shows an arrow in place of the line, with the number of hidden saves
    /// under it; clicking the arrow expands it.
    /// </summary>
    public class TreeView : MonoBehaviour
    {
        private const float Indent = 14f;
        private const int MaxLevels = 10;
        private const float LineWidth = 3f;
        private const float CapInset = 6f;
        private const float CapLength = 8f;
        private const float ArrowSize = 10f;
        private const float CountFontSize = 11f;
        public const string RailPrefix = "STRail";

        // One color per depth, starting with the first nested level.
        private static readonly Color[] depthColors =
        {
            new Color(1.00f, 0.72f, 0.30f),
            new Color(0.40f, 0.76f, 1.00f),
            new Color(0.56f, 0.90f, 0.45f),
            new Color(0.95f, 0.50f, 0.75f),
            new Color(0.80f, 0.70f, 1.00f),
            new Color(0.95f, 0.90f, 0.40f),
        };

        private class Row
        {
            public GameObject go;
            public TreeRow info;
            public HorizontalLayoutGroup layout;
            public RectOffset basePadding;
            public TMP_Text label;
            public string baseLabel;
            public readonly List<GameObject> drawn = new List<GameObject>();
        }

        private string folder;
        private SaveIndex index;
        private TMP_InputField search;
        private readonly List<Row> rows = new List<Row>();
        // Group number to the save that starts it, and the reverse of what
        // the index remembers about that save.
        private readonly Dictionary<int, string> groupHead = new Dictionary<int, string>();
        private readonly HashSet<int> collapsed = new HashSet<int>();
        private readonly Dictionary<int, int> groupSize = new Dictionary<int, int>();

        private static Sprite arrowSprite;

        /// <summary>Start over for a list the dialog has just rebuilt.</summary>
        public void Begin(LoadGameDialog dialog, string folder, SaveIndex index)
        {
            this.folder = folder;
            this.index = index;
            rows.Clear();
            groupHead.Clear();
            groupSize.Clear();
            collapsed.Clear();

            if (search == null)
            {
                search = dialog.GetComponentInChildren<TMP_InputField>(true);
                // The dialog's search shows and hides rows itself, and says
                // so afterwards. Collapsed rows it has just shown again are
                // hidden again then.
                LoadGameSearch searcher = dialog.GetComponent<LoadGameSearch>();
                if (searcher != null) searcher.hasFiltered += OnSearchFiltered;
            }
        }

        public void Add(GameObject go, TreeRow info)
        {
            var row = new Row { go = go, info = info };
            row.layout = go.GetComponentInChildren<HorizontalLayoutGroup>(true);
            if (row.layout != null) row.basePadding = row.layout.padding;

            // The save's name is the first label in the row's column of
            // labels, which is also where the dialog's search looks.
            VerticalLayoutGroup labels = go.GetComponentInChildren<VerticalLayoutGroup>(true);
            if (labels != null && labels.transform.childCount > 0)
                row.label = labels.transform.GetChild(0).GetComponent<TMP_Text>();
            if (row.label != null) row.baseLabel = row.label.text;
            rows.Add(row);

            if (info.path == null) return;
            if (info.groupStart)
            {
                groupHead[info.group] = info.name;
                SaveEntry e;
                if (info.depth > 0 && index.entries.TryGetValue(info.name, out e) && e.collapsed)
                    collapsed.Add(info.group);
            }
            // Every row counts toward each nested group around it.
            for (int level = 1; level < info.path.Length; level++)
            {
                int n;
                groupSize.TryGetValue(info.path[level], out n);
                groupSize[info.path[level]] = n + 1;
            }
        }

        private bool Searching
        {
            get { return search != null && !string.IsNullOrEmpty(search.text) && search.text.Trim().Length > 0; }
        }

        private void OnSearchFiltered(bool filtered)
        {
            Refresh();
        }

        /// <summary>
        /// True if the row is hidden by a collapsed group: any nested group
        /// around it is collapsed and the row is not that group's first.
        /// </summary>
        private bool HiddenByCollapse(TreeRow info)
        {
            if (info.path == null) return false;
            for (int level = 1; level < info.path.Length; level++)
            {
                if (!collapsed.Contains(info.path[level])) continue;
                if (level == info.depth && info.groupStart) continue;
                return true;
            }
            return false;
        }

        /// <summary>Apply visibility and redraw every row's lines.</summary>
        public void Refresh()
        {
            // A search shows every match wherever it is, so while one is
            // active the tree is drawn fully expanded and visibility is left
            // as the search set it.
            bool searching = Searching;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (row.go == null) continue;
                if (!searching) row.go.SetActive(!HiddenByCollapse(row.info));
                Draw(row, searching);
            }
        }

        private void Draw(Row row, bool searching)
        {
            for (int i = 0; i < row.drawn.Count; i++)
                if (row.drawn[i] != null) Destroy(row.drawn[i]);
            row.drawn.Clear();

            TreeRow info = row.info;
            int levels = Mathf.Min(info.depth, MaxLevels);
            bool isCollapsedHead = !searching && info.path != null && info.depth > 0 && info.groupStart && collapsed.Contains(info.group);

            if (row.label != null)
            {
                string text = row.baseLabel;
                if (info.inferred) text += "  <color=#a0a0a0><i>" + Localizer.Format("#LOC_SaveTimelines_Inferred") + "</i></color>";
                row.label.text = text;
            }

            // The row's icon and labels sit in one horizontal layout. Padding
            // it moves them right without changing the row's own size, which
            // the list's grid fixes.
            if (row.layout != null)
            {
                RectOffset p = row.basePadding;
                row.layout.padding = new RectOffset(p.left + Mathf.RoundToInt(levels * Indent), p.right, p.top, p.bottom);
            }

            if (info.path == null) return;
            for (int level = 1; level <= levels; level++)
            {
                int group = info.path[level];
                bool own = level == info.depth;
                bool start = own && info.groupStart;
                bool end = own && info.groupEnd;
                Color color = depthColors[(level - 1) % depthColors.Length];

                // The whole column is the click target; the line in it is
                // too thin to aim at.
                var column = new GameObject(RailPrefix + level, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
                column.layer = row.go.layer;
                column.transform.SetParent(row.go.transform, false);
                column.GetComponent<LayoutElement>().ignoreLayout = true;
                Image hit = column.GetComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;
                Button button = column.GetComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(delegate { Toggle(group); });
                var rect = (RectTransform)column.transform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = new Vector2((level - 1) * Indent, 0f);
                rect.offsetMax = new Vector2(level * Indent, 0f);
                row.drawn.Add(column);

                float x = (Indent - LineWidth) * 0.5f;
                if (own && isCollapsedHead)
                {
                    // The arrow's left edge is where a line's would be.
                    RectTransform arrow = AddImage(column, color, ArrowSprite);
                    arrow.anchorMin = arrow.anchorMax = new Vector2(0f, 0.5f);
                    arrow.pivot = new Vector2(0f, 0.5f);
                    arrow.anchoredPosition = new Vector2(x, 0f);
                    arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);

                    // How many saves are hidden, under the arrow, where a
                    // long save name cannot push it out of view.
                    int size;
                    groupSize.TryGetValue(group, out size);
                    if (row.label != null && size > 1)
                    {
                        var go = new GameObject("Count", typeof(RectTransform), typeof(CanvasRenderer));
                        go.layer = column.layer;
                        go.transform.SetParent(column.transform, false);
                        TextMeshProUGUI count = go.AddComponent<TextMeshProUGUI>();
                        count.font = row.label.font;
                        count.fontSize = CountFontSize;
                        count.color = color;
                        count.alignment = TextAlignmentOptions.TopLeft;
                        count.enableWordWrapping = false;
                        count.overflowMode = TextOverflowModes.Overflow;
                        count.raycastTarget = false;
                        count.text = "+" + (size - 1);
                        var countRect = (RectTransform)go.transform;
                        countRect.anchorMin = countRect.anchorMax = new Vector2(0f, 0.5f);
                        countRect.pivot = new Vector2(0f, 1f);
                        countRect.anchoredPosition = new Vector2(x, -ArrowSize * 0.5f - 1f);
                        countRect.sizeDelta = new Vector2(40f, CountFontSize + 4f);
                    }
                    continue;
                }

                RectTransform line = AddImage(column, color, null);
                line.anchorMin = new Vector2(0f, 0f);
                line.anchorMax = new Vector2(0f, 1f);
                line.pivot = new Vector2(0f, 0.5f);
                line.offsetMin = new Vector2(x, end ? CapInset : 0f);
                line.offsetMax = new Vector2(x + LineWidth, start ? -CapInset : 0f);

                if (start)
                {
                    RectTransform cap = AddImage(column, color, null);
                    cap.anchorMin = cap.anchorMax = new Vector2(0f, 1f);
                    cap.pivot = new Vector2(0f, 1f);
                    cap.anchoredPosition = new Vector2(x, -CapInset);
                    cap.sizeDelta = new Vector2(CapLength, LineWidth);
                }
                if (end)
                {
                    RectTransform cap = AddImage(column, color, null);
                    cap.anchorMin = cap.anchorMax = new Vector2(0f, 0f);
                    cap.pivot = new Vector2(0f, 0f);
                    cap.anchoredPosition = new Vector2(x, CapInset);
                    cap.sizeDelta = new Vector2(CapLength, LineWidth);
                }
            }
        }

        private void Toggle(int group)
        {
            string head;
            if (!groupHead.TryGetValue(group, out head)) return;
            bool now = !collapsed.Contains(group);
            if (now) collapsed.Add(group);
            else collapsed.Remove(group);
            Tracker.SetCollapsed(folder, head, now);
            Refresh();
        }

        private static RectTransform AddImage(GameObject parent, Color color, Sprite sprite)
        {
            var go = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.layer;
            go.transform.SetParent(parent.transform, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>A right-pointing triangle, drawn once at run time.</summary>
        private static Sprite ArrowSprite
        {
            get
            {
                if (arrowSprite != null) return arrowSprite;
                const int size = 32;
                var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color32[size * size];
                float middle = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        // Full height at the left edge, narrowing to a point.
                        float half = (size - 1 - px) * 0.5f;
                        float alpha = Mathf.Clamp01(half - Mathf.Abs(y - middle) + 0.5f);
                        pixels[y * size + px] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                arrowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                arrowSprite.hideFlags = HideFlags.HideAndDontSave;
                return arrowSprite;
            }
        }
    }
}
