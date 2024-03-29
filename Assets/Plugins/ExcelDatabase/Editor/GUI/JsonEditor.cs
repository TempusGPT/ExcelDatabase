using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelDatabase.Editor.GUI
{
    public class JsonEditor : EditorWindow
    {
        private string jsonPath;
        private int selectedIndex;
        private Dictionary<string, object>[] table;

        public static void Open(string jsonPath, string name)
        {
            var window = GetWindow<JsonEditor>();
            window.titleContent = new($"{name} - Excel Database");
            window.jsonPath = jsonPath;
            window.selectedIndex = 0;
            Refresh();
        }

        public static void Refresh()
        {
            if (!HasOpenInstances<JsonEditor>())
            {
                return;
            }

            var window = GetWindow<JsonEditor>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(window.jsonPath);
            window.table = JsonConvert.DeserializeObject<Dictionary<string, object>[]>(json.text);

            window.ListIDs();
            window.ListColumns();
        }

        public void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Plugins/ExcelDatabase/Editor/GUI/JsonEditor.uxml"
            );
            rootVisualElement.Add(visualTree.Instantiate());

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Plugins/ExcelDatabase/Editor/GUI/Style.uss"
            );
            rootVisualElement.styleSheets.Add(styleSheet);

            var splitView = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            var idList = new ListView { name = "id-list" };
            var columnList = new ListView
            {
                name = "column-list",
                selectionType = SelectionType.None
            };

            splitView.Add(idList);
            splitView.Add(columnList);
            rootVisualElement.Add(splitView);
        }

        private void ListIDs()
        {
            var idList = rootVisualElement.Q<ListView>("id-list");
            idList.selectionChanged += HandleSelectionChanged;
            idList.SetSelection(selectedIndex);

            // Prevent KeyNotFoundException when scroll
            idList.bindItem = null;
            idList.itemsSource = table;
            idList.makeItem = MakeItem;
            idList.bindItem = BindItem;

            VisualElement MakeItem()
            {
                var label = new Label();
                label.AddToClassList("list-label");
                return label;
            }

            void BindItem(VisualElement element, int i)
            {
                if (element is Label label)
                {
                    label.text = table[i]["ID"] as string;
                }
            }

            void HandleSelectionChanged(IEnumerable<object> _)
            {
                selectedIndex = idList.selectedIndex;
                ListColumns();
            }
        }

        private void ListColumns()
        {
            var columns = table[selectedIndex];
            var columnList = rootVisualElement.Q<ListView>("column-list");

            // Prevent KeyNotFoundException when scroll
            columnList.bindItem = null;
            columnList.itemsSource = columns.Skip(1).ToList();
            columnList.makeItem = MakeItem;
            columnList.bindItem = BindItem;

            VisualElement MakeItem()
            {
                var field = new TextField { multiline = true };
                field.AddToClassList("list-field");
                field.RegisterValueChangedCallback(OnValueChanged);
                field.RegisterCallback<FocusOutEvent>(OnFocusOut);
                return field;

                void OnValueChanged(ChangeEvent<string> e)
                {
                    if (e.target is TextField field)
                    {
                        columns[field.label] =
                            columns[field.label] is JArray
                                ? new JArray(e.newValue.Split('\n'))
                                : e.newValue;
                    }
                }

                void OnFocusOut(FocusOutEvent _)
                {
                    var json = JsonConvert.SerializeObject(table, Formatting.Indented);
                    File.WriteAllText(jsonPath, json);
                    AssetDatabase.Refresh();
                }
            }

            void BindItem(VisualElement element, int i)
            {
                if (element is TextField field)
                {
                    var column = columns.Skip(1).ElementAt(i);
                    field.label = column.Key;
                    field.value = column.Value is JArray array
                        ? string.Join('\n', array)
                        : column.Value as string;
                }
            }
        }
    }
}
