using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExcelDatabase.Editor.Library;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ExcelDatabase.Editor.GUI
{
    public class TableList : EditorWindow
    {
        private static IEnumerable<TableParser.Result> selectedResults;

        private static readonly Lazy<SortedSet<TableParser.Result>> lazyResultSet =
            new(CreateResultSet);
        private static SortedSet<TableParser.Result> resultSet => lazyResultSet.Value;

        private static string resultPath => $"{TableParser.root}/Dist/ParseResult.json";

        [MenuItem("Tools/Excel Database/Show Table List")]
        public static void ShowTableList()
        {
            var window = GetWindow<TableList>();
            window.titleContent = new("Table List - Excel Database");
        }

        [MenuItem("Tools/Excel Database/Parse Convert Tables")]
        public static void ParseConvertTables()
        {
            ParseTables(Selection.objects, TableType.Convert);
        }

        [MenuItem("Tools/Excel Database/Parse Enum Tables")]
        public static void ParseEnumTables()
        {
            ParseTables(Selection.objects, TableType.Enum);
        }

        [MenuItem("Tools/Excel Database/Parse Variable Tables")]
        public static void ParseVariableTables()
        {
            ParseTables(Selection.objects, TableType.Variable);
        }

        public void CreateGUI()
        {
            ApplyUI();
            RegisterButtons();
            ListTables();
        }

        private static SortedSet<TableParser.Result> CreateResultSet()
        {
            return File.Exists(resultPath)
                ? JsonConvert.DeserializeObject<SortedSet<TableParser.Result>>(
                    File.ReadAllText(resultPath)
                )
                : new();
        }

        private static void ParseTables(IEnumerable<Object> files, TableType type)
        {
            foreach (var file in files.Where(IsExcelFile))
            {
                try
                {
                    var result = type switch
                    {
                        TableType.Convert => TableParser.ParseConvert(file),
                        TableType.Enum => TableParser.ParseEnum(file),
                        TableType.Variable => TableParser.ParseVariable(file),
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    resultSet.Add(result);
                    SyncResultSet();
                }
                catch (TableParser.Exception e)
                {
                    Debug.LogError($"{e.tableName}: {e.Message}");
                }
                catch (ArgumentOutOfRangeException)
                {
                    Debug.LogError($"{file.name}: Please remove and parse again");
                }
            }

            AssetDatabase.Refresh();
            JsonEditor.Refresh();
            Debug.Log("Excel Database: Parsing has been completed");

            static bool IsExcelFile(Object file)
            {
                var path = AssetDatabase.GetAssetPath(file);
                return Path.GetExtension(path) == ".xlsx"
                    && !Path.GetFileName(path).StartsWith(TableParser.excludePrefix);
            }
        }

        private static void RemoveTables(IEnumerable<TableParser.Result> tables)
        {
            foreach (var table in tables)
            {
                resultSet.Remove(table);
                AssetDatabase.DeleteAsset(TableParser.DistPath(table.name, table.type));

                if (table.type == TableType.Convert)
                {
                    AssetDatabase.DeleteAsset(TableParser.JsonPath(table.name));
                }
            }

            SyncResultSet();
            Debug.Log("Excel Database: Removing has been completed");
        }

        private static void SyncResultSet()
        {
            var json = JsonConvert.SerializeObject(resultSet, Formatting.Indented);
            File.WriteAllText(resultPath, json);
        }

        private void ApplyUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Plugins/ExcelDatabase/Editor/GUI/TableList.uxml"
            );
            rootVisualElement.Add(visualTree.Instantiate());

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Plugins/ExcelDatabase/Editor/GUI/Style.uss"
            );
            rootVisualElement.styleSheets.Add(styleSheet);
        }

        private void RegisterButtons()
        {
            var editButton = rootVisualElement.Q<Button>("edit-button");
            editButton.RegisterCallback<ClickEvent>(HandleEdit);
            editButton.SetEnabled(false);

            var parseButton = rootVisualElement.Q<Button>("parse-button");
            parseButton.RegisterCallback<ClickEvent>(HandleParse);
            parseButton.SetEnabled(false);

            var removeButton = rootVisualElement.Q<Button>("remove-button");
            removeButton.RegisterCallback<ClickEvent>(HandleRemove);
            removeButton.SetEnabled(false);

            void HandleEdit(ClickEvent _)
            {
                var resultName = selectedResults.First().name;
                JsonEditor.Open(TableParser.JsonPath(resultName), resultName);
            }

            void HandleParse(ClickEvent _)
            {
                foreach (var parseResults in selectedResults.GroupBy(table => table.type))
                {
                    var files = parseResults.Select(
                        (table) => AssetDatabase.LoadAssetAtPath<Object>(table.excelPath)
                    );
                    ParseTables(files, parseResults.Key);
                }
            }

            void HandleRemove(ClickEvent _)
            {
                RemoveTables(selectedResults);
            }
        }

        private void ListTables()
        {
            var listView = rootVisualElement.Q<ListView>();
            // 스크롤 시 KeyNotFoundException 방지
            listView.bindItem = null;

            listView.itemsSource = resultSet.ToList();
            listView.makeItem = MakeItem;
            listView.bindItem = BindItem;
            listView.selectionChanged += HandleSelectionChanged;

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
                    label.text = resultSet.ElementAt(i).ToString();
                }
            }

            void HandleSelectionChanged(IEnumerable<object> selection)
            {
                selectedResults = selection.Cast<TableParser.Result>();
                var editButton = rootVisualElement.Q<Button>("edit-button");
                editButton.SetEnabled(
                    selectedResults?.Count() == 1
                        && selectedResults?.First().type == TableType.Convert
                );

                var parseButton = rootVisualElement.Q<Button>("parse-button");
                parseButton.SetEnabled(selectedResults?.Count() > 0);
                var removeButton = rootVisualElement.Q<Button>("remove-button");
                removeButton.SetEnabled(selectedResults?.Count() > 0);
            }
        }
    }
}
