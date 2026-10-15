using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace CampusCafe.API
{
    /// <summary>
    /// Small helpers shared by the controllers that write payments, receipts and deliveries.
    /// - Reads real column names/types from the .accdb so inserts never reference a missing column.
    /// - Generates the next ID (the tables use plain INT primary keys, not AutoNumber).
    /// - Sends DateTime / Currency / Text values in the type the column actually uses.
    /// </summary>
    public static class DbHelper
    {
        public const string FallbackConnection =
            @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\alcan\Downloads\Physical Data Model (GILO, PAMATIAN, VILLAFOR, VELASCO).accdb;";

        /// <summary>Column name -> OLE DB data type code. Empty when the table does not exist.</summary>
        public static Dictionary<string, int> GetColumns(OleDbConnection conn, string table)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            DataTable? schema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, new object?[] { null, null, table, null });
            if (schema != null)
            {
                foreach (DataRow row in schema.Rows)
                {
                    string name = row["COLUMN_NAME"]?.ToString() ?? "";
                    int type = row["DATA_TYPE"] != DBNull.Value ? Convert.ToInt32(row["DATA_TYPE"]) : 0;
                    if (name.Length > 0) map[name] = type;
                }
            }
            return map;
        }

        public static void RequireTable(Dictionary<string, int> cols, string tableName)
        {
            if (cols.Count == 0)
                throw new InvalidOperationException($"Table '{tableName}' was not found in the database.");
        }

        private static bool IsTextType(int oleDbType)
        {
            // 8 BSTR, 129 CHAR, 130 WCHAR, 200 VARCHAR, 201 LONGVARCHAR, 202 VARWCHAR, 203 LONGVARWCHAR
            return oleDbType == 8 || oleDbType == 129 || oleDbType == 130 ||
                   oleDbType == 200 || oleDbType == 201 || oleDbType == 202 || oleDbType == 203;
        }

        public static OleDbParameter MakeParam(object? value)
        {
            switch (value)
            {
                case null:
                    return new OleDbParameter("?", DBNull.Value);
                case DateTime dt:
                    return new OleDbParameter("?", OleDbType.Date) { Value = dt };
                case decimal dec:
                    return new OleDbParameter("?", OleDbType.Currency) { Value = dec };
                case double dbl:
                    return new OleDbParameter("?", OleDbType.Double) { Value = dbl };
                case int i:
                    return new OleDbParameter("?", OleDbType.Integer) { Value = i };
                case string s:
                    return new OleDbParameter("?", OleDbType.VarWChar) { Value = s };
                default:
                    return new OleDbParameter("?", value);
            }
        }

        private static OleDbParameter MakeParamForColumn(object? value, int columnType)
        {
            if (value != null && IsTextType(columnType) && value is not string)
            {
                string text = value is DateTime dt
                    ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return MakeParam(text);
            }
            return MakeParam(value);
        }

        /// <summary>INSERT only the columns that really exist in the table.</summary>
        public static void Insert(OleDbConnection conn, OleDbTransaction tx, string table,
            Dictionary<string, int> cols, params (string Name, object? Value)[] values)
        {
            var names = new List<string>();
            using var cmd = new OleDbCommand { Connection = conn, Transaction = tx };

            foreach (var (name, value) in values)
            {
                if (!cols.TryGetValue(name, out int colType)) continue;
                names.Add($"[{name}]");
                cmd.Parameters.Add(MakeParamForColumn(value, colType));
            }

            if (names.Count == 0)
                throw new InvalidOperationException($"No matching columns found to insert into '{table}'.");

            string placeholders = string.Join(", ", names.Select(_ => "?"));
            cmd.CommandText = $"INSERT INTO [{table}] ({string.Join(", ", names)}) VALUES ({placeholders})";
            cmd.ExecuteNonQuery();
        }

        public static int NextId(OleDbConnection conn, OleDbTransaction? tx, string table, string idColumn)
        {
            object? res = Scalar(conn, tx, $"SELECT MAX([{idColumn}]) FROM [{table}]");
            return (res == null || res == DBNull.Value) ? 1 : Convert.ToInt32(res) + 1;
        }

        public static object? Scalar(OleDbConnection conn, OleDbTransaction? tx, string sql, params object?[] args)
        {
            using var cmd = new OleDbCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.Add(MakeParam(a));
            return cmd.ExecuteScalar();
        }

        public static int NonQuery(OleDbConnection conn, OleDbTransaction? tx, string sql, params object?[] args)
        {
            using var cmd = new OleDbCommand(sql, conn, tx);
            foreach (var a in args) cmd.Parameters.Add(MakeParam(a));
            return cmd.ExecuteNonQuery();
        }

        public static bool Exists(OleDbConnection conn, OleDbTransaction? tx, string sql, params object?[] args)
        {
            object? res = Scalar(conn, tx, sql, args);
            return res != null && res != DBNull.Value && Convert.ToInt32(res) > 0;
        }

        /// <summary>Current time without milliseconds (Access stores seconds precision).</summary>
        public static DateTime Now()
        {
            var n = DateTime.Now;
            return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second);
        }

        public static string Fmt(object? value)
        {
            if (value == null || value == DBNull.Value) return "";
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return value.ToString() ?? "";
        }

        public static decimal Dec(object? value)
        {
            return (value == null || value == DBNull.Value) ? 0m : Convert.ToDecimal(value);
        }

        /// <summary>Whole-number view of a numeric DB value (old rows may still hold decimals).</summary>
        public static int Whole(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;
            return (int)Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);
        }

        /// <summary>First of the given column names that really exists in the table (or null).</summary>
        public static string? FirstExisting(Dictionary<string, int> cols, params string[] names)
        {
            foreach (var n in names)
                if (cols.ContainsKey(n)) return n;
            return null;
        }

        /// <summary>Adds a column to a table when it is missing (e.g. Employee.Position).</summary>
        public static void EnsureColumn(OleDbConnection conn, Dictionary<string, int> cols,
            string table, string column, string sqlType)
        {
            if (cols.ContainsKey(column)) return;
            NonQuery(conn, null, $"ALTER TABLE [{table}] ADD COLUMN [{column}] {sqlType}");
            cols[column] = 202; // VARWCHAR - so the caller can keep using the same dictionary
        }

        /// <summary>
        /// Finds an ingredient by ID, or - when the user typed a name that is not in the list -
        /// by name (case-insensitive), and creates it (stock 0) if it does not exist yet.
        /// Returns the IngredientID, plus whether a brand-new ingredient row was created.
        /// </summary>
        public static (int Id, bool Created) ResolveIngredient(OleDbConnection conn, OleDbTransaction tx,
            int ingredientId, string? typedName, string? typedUnit)
        {
            if (ingredientId > 0)
            {
                if (!Exists(conn, tx, "SELECT COUNT(*) FROM Ingredient WHERE IngredientID = ?", ingredientId))
                    throw new InvalidOperationException($"Ingredient #{ingredientId} not found.");
                return (ingredientId, false);
            }

            string name = (typedName ?? "").Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Please choose or type an ingredient name.");
            if (name.Length > 100)
                throw new InvalidOperationException("Ingredient name is too long (max 100 characters).");

            // already exists? (compare ignoring upper/lower case)
            object? found = Scalar(conn, tx,
                "SELECT IngredientID FROM Ingredient WHERE UCase(IngredientName) = ?", name.ToUpperInvariant());
            if (found != null && found != DBNull.Value)
                return (Convert.ToInt32(found), false);

            string unit = string.IsNullOrWhiteSpace(typedUnit) ? "pcs" : typedUnit.Trim();
            if (unit.Length > 30) unit = unit.Substring(0, 30);

            int newId = NextId(conn, tx, "Ingredient", "IngredientID");
            NonQuery(conn, tx,
                "INSERT INTO Ingredient (IngredientID, IngredientName, UnitOfMeasurement, CurrentQuantity, MinimumStockLevel) VALUES (?, ?, ?, ?, ?)",
                newId, name, unit, 0, 10);
            return (newId, true);
        }

        /// <summary>Stock status used everywhere: 0 = Unavailable, 1-9 = Low stock, 10+ = Available.</summary>
        public static string StockStatus(int quantity)
        {
            if (quantity <= 0) return "Unavailable";
            if (quantity < 10) return "Low stock";
            return "Available";
        }

        public static void SafeRollback(OleDbTransaction? tx)
        {
            try { tx?.Rollback(); } catch { /* connection may already be closed */ }
        }
    }
}
