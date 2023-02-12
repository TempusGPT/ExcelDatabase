using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDatabase.Editor.Library;
using Newtonsoft.Json;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEditor;
using Object = UnityEngine.Object;

namespace ExcelDatabase.Editor.Parser
{
    public class ConvertParser : IParsable
    {
        private const int NameRow = 0;
        private const int TypeRow = 1;
        private const int IDCol = 0;

        private const string ExcludePrefix = "#";
        private const string ArraySeparator = "\n";

        private const string ColTemplate = "#COL#";
        private const string TableVariable = "$TABLE$";
        private const string TypeVariable = "$TYPE$";
        private const string NameVariable = "$NAME$";

        private static readonly string TablePath = $"{Config.TemplatePath}/Convert/Table.txt";
        private static readonly string GeneralColPath = $"{Config.TemplatePath}/Convert/GeneralCol.txt";
        private static readonly string ConvertColPath = $"{Config.TemplatePath}/Convert/ConvertCol.txt";
        private static readonly string GeneralArrayColPath = $"{Config.TemplatePath}/Convert/GeneralArrayCol.txt";
        private static readonly string ConvertArrayColPath = $"{Config.TemplatePath}/Convert/ConvertArrayCol.txt";

        private readonly ISheet _sheet;
        private readonly string _tableName;
        private readonly string _excelPath;

        public ConvertParser(Object file)
        {
            var path = AssetDatabase.GetAssetPath(file);
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
            _sheet = new XSSFWorkbook(stream).GetSheetAt(0);
            _tableName = ParseUtility.Format(file.name);
            _excelPath = AssetDatabase.GetAssetPath(file);
        }

        public ParseResult Parse()
        {
            var cols = ValidateCols().ToArray();
            var rows = ValidateRows(cols);
            var script = BuildScript(cols);
            var jsonPath = WriteJson(rows);
            var distPath = ParseUtility.WriteScript(TableType.Convert, _tableName, script);
            return new ParseResult(TableType.Convert, _tableName, _excelPath, new[] { distPath, jsonPath });
        }

        private IEnumerable<Col> ValidateCols()
        {
            var nameRow = _sheet.GetRow(NameRow);
            var typeRow = _sheet.GetRow(TypeRow);
            if (nameRow.GetCellValue(IDCol) != "ID" || typeRow.GetCellValue(0) != "string")
            {
                throw new ParseFailureException(_tableName, "Invalid ID column");
            }

            var diffChecker = new HashSet<string>();
            for (var i = 1; i <= nameRow.LastCellNum; i++)
            {
                var col = new Col(i, nameRow.GetCellValue(i), typeRow.GetCellValue(i));
                if (col.Name.StartsWith(ExcludePrefix))
                {
                    continue;
                }

                if (col.Name == string.Empty)
                {
                    break;
                }

                if (char.IsDigit(col.Name, 0))
                {
                    throw new ParseFailureException(_tableName,
                        $"Column name '{col.Name}' starts with a number");
                }

                if (!diffChecker.Add(col.Name))
                {
                    throw new ParseFailureException(_tableName, $"Duplicate column name '{col.Name}'");
                }

                bool TypeExists(string type)
                {
                    var systemType = Type.GetType(
                        $"ExcelDatabase.{type.Replace('.', '+')}, Assembly-CSharp-firstpass");
                    return systemType != null;
                }

                switch (col.TypeSpec)
                {
                    case Col.TypeSpecification.Variable:
                        break;

                    case Col.TypeSpecification.None:
                    case Col.TypeSpecification.Primitive when !ParseUtility.TypeValidators.ContainsKey(col.Type):
                    case Col.TypeSpecification.Convert when !TypeExists(col.Type + "Type"):
                    case Col.TypeSpecification.Enum when !TypeExists(col.Type):
                        throw new ParseFailureException(_tableName,
                            $"Column type '{col.Type}' in '{col.Name}' is invalid");

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                yield return col;
            }
        }

        private IEnumerable<Row> ValidateRows(Col[] cols)
        {
            var diffChecker = new HashSet<string>();
            for (var i = 2; i <= _sheet.LastRowNum; i++)
            {
                var row = new Row(_sheet.GetRow(i).GetCellValue(IDCol));
                if (row.ID == string.Empty)
                {
                    break;
                }

                if (!diffChecker.Add(row.ID))
                {
                    throw new ParseFailureException(_tableName, $"Duplicate ID '{row.ID}'");
                }

                foreach (var col in cols)
                {
                    var cell = _sheet.GetRow(i).GetCellValue(col.Index);
                    if (cell.StartsWith(ExcludePrefix))
                    {
                        continue;
                    }

                    if (cell == string.Empty)
                    {
                        throw new ParseFailureException(_tableName,
                            $"An empty cell exists in '{col.Name}' of '{row.ID}'");
                    }

                    var cellValues = cell.Split(ArraySeparator);
                    if (!col.IsArray && cellValues.Length > 1)
                    {
                        throw new ParseFailureException(_tableName,
                            $"The cell in '{col.Name}' of '{row.ID}' is array, but its type is not an array");
                    }

                    if (col.TypeSpec == Col.TypeSpecification.Primitive &&
                        cellValues.Any(cellValue => !ParseUtility.TypeValidators[col.Type](cellValue)))
                    {
                        throw new ParseFailureException(_tableName,
                            $"The cell in '{col.Name}' of '{row.ID}' type mismatch");
                    }

                    if (col.TypeSpec == Col.TypeSpecification.Enum)
                    {
                        var type = Type.GetType(
                            $"ExcelDatabase.{col.Type.Replace('.', '+')}, Assembly-CSharp-firstpass");
                        if (type == null || cellValues.Any(cellValue => !Enum.IsDefined(type, cellValue)))
                        {
                            throw new ParseFailureException(_tableName,
                                $"The cell in '{col.Name}' of '{row.ID}' type mismatch");
                        }
                    }

                    row.Cells[col.TypeSpec == Col.TypeSpecification.Convert ? "_id" + col.Name : col.Name] = cell;
                }

                yield return row;
            }
        }

        private string BuildScript(IEnumerable<Col> cols)
        {
            var tableTemplate = File.ReadAllText(TablePath);
            var generalColTemplate = File.ReadAllText(GeneralColPath);
            var convertColTemplate = File.ReadAllText(ConvertColPath);
            var generalArrayColTemplate = File.ReadAllText(GeneralArrayColPath);
            var convertArrayColTemplate = File.ReadAllText(ConvertArrayColPath);
            var builder = new StringBuilder(tableTemplate).Replace(TableVariable, _tableName);

            foreach (var col in cols)
            {
                if (col.TypeSpec == Col.TypeSpecification.Convert)
                {
                    builder.Replace(ColTemplate,
                        (col.IsArray ? convertArrayColTemplate : convertColTemplate) + ColTemplate);
                }
                else
                {
                    builder.Replace(ColTemplate,
                        (col.IsArray ? generalArrayColTemplate : generalColTemplate) + ColTemplate);
                }

                builder
                    .Replace(TypeVariable, col.Type)
                    .Replace(NameVariable, col.Name);
            }

            builder.Replace(ColTemplate, string.Empty);
            return builder.ToString();
        }

        private string WriteJson(IEnumerable<Row> rows)
        {
            var json = JsonConvert.SerializeObject(rows);
            const string distDirectory = "Assets/Resources/ExcelDatabase";
            if (!Directory.Exists(distDirectory))
            {
                Directory.CreateDirectory(distDirectory);
            }

            var distPath = $"{distDirectory}/{_tableName}.json";
            File.WriteAllText(distPath, json);
            return distPath;
        }

        private readonly struct Col
        {
            public readonly int Index;
            public readonly string Name;
            public readonly string Type;
            public readonly bool IsArray;
            public readonly TypeSpecification TypeSpec;

            public Col(int index, string name, string type)
            {
                Index = index;
                Name = ParseUtility.Format(name);
                Type = ParseUtility.Format(type);
                IsArray = type.EndsWith("[]");

                TypeSpec = ParseUtility.TypeValidators.ContainsKey(Type) switch
                {
                    true => TypeSpecification.Primitive,
                    false when Type.StartsWith("Tb") => TypeSpecification.Convert,
                    false when Type.StartsWith("Em") => TypeSpecification.Enum,
                    false when Type.StartsWith("Va") => TypeSpecification.Variable,
                    _ => TypeSpecification.None
                };
            }

            public enum TypeSpecification
            {
                None,
                Primitive,
                Convert,
                Enum,
                Variable
            }
        }

        private readonly struct Row
        {
            public readonly string ID;
            public readonly Dictionary<string, object> Cells;

            public Row(string id)
            {
                ID = id;
                Cells = new Dictionary<string, object> { { "ID", id } };
            }
        }
    }
}