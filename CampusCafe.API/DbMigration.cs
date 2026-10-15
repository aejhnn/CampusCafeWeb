using System.Data.OleDb;

namespace CampusCafe.API
{
    /// <summary>
    /// Runs once when the API starts. Makes the stock / delivery quantity columns in the
    /// .accdb whole numbers (Long Integer) instead of DOUBLE, so the database itself can
    /// no longer hold decimal quantities.
    ///
    /// It is safe to run on every start: columns that are already whole numbers are skipped,
    /// and if anything fails (for example the file is open exclusively in Access) it only
    /// logs a warning - the API keeps working and still writes whole numbers.
    /// Turn it off with  "Database": { "WholeNumberQuantities": false }  in appsettings.json.
    /// </summary>
    public static class DbMigration
    {
        // OLE DB type codes: 2 = SmallInt, 3 = Integer (Long), 17 = UnsignedTinyInt
        private static readonly int[] WholeTypes = { 2, 3, 17 };

        public static void Run(string connectionString, ILogger logger)
        {
            try
            {
                using var conn = new OleDbConnection(connectionString);
                conn.Open();
                MakeWholeNumber(conn, logger, "Ingredient", "IngredientID", "CurrentQuantity");
                MakeWholeNumber(conn, logger, "Delivery_Item", "DeliveryItemID", "QuantityReceived");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Whole-number migration skipped: {Message}", ex.Message);
            }
        }

        private static void MakeWholeNumber(OleDbConnection conn, ILogger logger,
            string table, string idColumn, string column)
        {
            try
            {
                var cols = DbHelper.GetColumns(conn, table);
                if (!cols.TryGetValue(column, out int type)) return;

                // 1. Round any decimal values that are already stored.
                var toFix = new List<(int Id, int Rounded)>();
                using (var cmd = new OleDbCommand($"SELECT [{idColumn}], [{column}] FROM [{table}]", conn))
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        if (rd[0] == DBNull.Value || rd[1] == DBNull.Value) continue;
                        double v = Convert.ToDouble(rd[1]);
                        int whole = DbHelper.Whole(v);
                        if (Math.Abs(v - whole) > 0.0000001) toFix.Add((Convert.ToInt32(rd[0]), whole));
                    }
                }
                foreach (var (id, rounded) in toFix)
                    DbHelper.NonQuery(conn, null, $"UPDATE [{table}] SET [{column}] = ? WHERE [{idColumn}] = ?", rounded, id);
                if (toFix.Count > 0)
                    logger.LogInformation("{Table}.{Column}: rounded {Count} decimal value(s) to whole numbers.", table, column, toFix.Count);

                // 2. Change the column type so the .accdb only accepts whole numbers.
                if (!WholeTypes.Contains(type))
                {
                    DbHelper.NonQuery(conn, null, $"ALTER TABLE [{table}] ALTER COLUMN [{column}] LONG NOT NULL");
                    logger.LogInformation("{Table}.{Column}: column type changed to Long Integer.", table, column);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not convert {Table}.{Column} to whole numbers: {Message}", table, column, ex.Message);
            }
        }
    }
}
