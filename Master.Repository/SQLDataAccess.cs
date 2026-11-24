using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.Caching;
using System.Runtime.InteropServices.ComTypes;

namespace Master.Repository
{
    public class SQLDataAccess : IDataAccess
    {
        private string _connectionString;

        private MemoryCache _cache;

        private static SQLDataAccess instance = null;
        public static SQLDataAccess GetInstance(string connectionString)
        {
            if (instance == null)
            {
                instance = new SQLDataAccess(connectionString);
            }
            return instance;
        }

        public SQLDataAccess(string connectionString)
        {
            _connectionString = connectionString;
            _cache = MemoryCache.Default;
        }

        public bool CheckDBConnection()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Database connection failed: " + ex.Message);
                return false;
            }
        }

        public Shift GetCurrentShift()
        {
            Shift shift = null;
            try
            {
                List<Shift> shiftData = GetShiftMasterData();
                TimeSpan timeOfDay = DateTime.Now.TimeOfDay;
                foreach (Shift item in shiftData)
                {
                    if (item.StartTime < item.EndTime)
                    {
                        if (timeOfDay >= item.StartTime && timeOfDay < item.EndTime)
                        {
                            shift = item;
                        }
                    }
                    else if (timeOfDay >= item.StartTime || timeOfDay < item.EndTime)
                    {
                        shift = item;
                    }
                }
            }
            catch (Exception ex)
            {
                //Logger.WriteLog(Logger.LogType.Error, "SqlUtility", "GetCurrentShift", "Exception Error", ex.ToString(), default(DateTime), "GetCurrentShift", 139);
            }

            return shift;
        }

        private string _shiftTable;

        /// <summary>
        /// The table holding the shift times: [Shift] (Master_Teal_db_Schema.sql), or [ShiftMasterData]
        /// in a database made for V3. Looked up once. Without the right name the current shift is
        /// null, and no run / down / idle time, shift status or OEE is recorded.
        /// </summary>
        private string ShiftTable()
        {
            if (_shiftTable != null)
                return _shiftTable;

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID(N'[TEAL_Master].[dbo].[Shift]', N'U') IS NOT NULL THEN N'Shift' ELSE N'ShiftMasterData' END",
                connection))
            {
                connection.Open();
                _shiftTable = (string)command.ExecuteScalar();
            }

            return _shiftTable;
        }

        public List<Shift> GetShiftMasterData()
        {
            if (_cache.Contains("MasterShiftData"))
            {
                return (List<Shift>)_cache.Get("MasterShiftData");
            }

            List<Shift> list = new List<Shift>();
            string cmdText = "SELECT Shift, StartTime, EndTime FROM [TEAL_Master].[dbo].[" + ShiftTable() + "]";
            using (SqlConnection sqlConnection = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(cmdText, sqlConnection))
            {
                sqlConnection.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                    {
                        list.Add(new Shift
                        {
                            ShiftName = reader["Shift"].ToString(),
                            StartTime = TimeSpan.Parse(reader["StartTime"].ToString()),
                            EndTime = TimeSpan.Parse(reader["EndTime"].ToString())
                        });
                    }

                _cache.Add("MasterShiftData", list, DateTimeOffset.Now.AddDays(1));
            }
            return list;
        }

        public void UpdateShiftMasterData(string shiftName, DateTime startTime, DateTime endTime)
        {
            string query = @"Update [TEAL_Master].[dbo].[" + ShiftTable() + @"]
                             SET [StartTime] = @StartTime,
                             [EndTime] = @EndTime
                             WHERE [Shift] = @Shift";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StartTime", startTime.ToString("HH:mm:ss"));
                command.Parameters.AddWithValue("@EndTime", endTime.ToString("HH:mm:ss"));
                command.Parameters.AddWithValue("@Shift", shiftName);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public void AddHealthData(HealthData healthRecord)
        {
            string query = @"INSERT INTO [TEAL_Master].[dbo].[HealthData] 
           ([Date] ,[Shift] ,[HealthCategory] ,[StartTime] ,[EndTime] ,[TotalTime])
                VALUES
           ( @Date, @Shift, @HealthCategory, @StartTime, @EndTime, @TotalTime)";


            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", healthRecord.Date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@Shift", healthRecord.Shift);
                command.Parameters.AddWithValue("@HealthCategory", healthRecord.HealthCategory);
                command.Parameters.AddWithValue("@StartTime", healthRecord.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@EndTime", healthRecord.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@TotalTime", healthRecord.TotalTime);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }
        public List<HealthData> GetHealthData(DateTime date, string shift)
        {
            List<HealthData> healthDataList = new List<HealthData>();

            string query = @"SELECT 
            [Date],
            [Shift],
            [HealthCategory],
            [StartTime],
            [EndTime],
            [TotalTime]
        FROM [TEAL_Master].[dbo].[HealthData]
        WHERE CAST([Date] AS DATE) = @Date
          AND [Shift] = @Shift";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.Add("@Date", SqlDbType.Date).Value = date.Date;
                command.Parameters.AddWithValue("@Shift", shift);

                connection.Open();

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        HealthData data = new HealthData
                        {
                            Date = reader.IsDBNull(reader.GetOrdinal("Date")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Date")),
                            Shift = reader.IsDBNull(reader.GetOrdinal("Shift")) ? null : reader.GetString(reader.GetOrdinal("Shift")),
                            HealthCategory = reader.IsDBNull(reader.GetOrdinal("HealthCategory")) ? null : reader.GetString(reader.GetOrdinal("HealthCategory")),
                            StartTime = reader.IsDBNull(reader.GetOrdinal("StartTime")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("StartTime")),
                            EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("EndTime")),
                            TotalTime = reader.IsDBNull(reader.GetOrdinal("TotalTime")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("TotalTime"))
                        };

                        healthDataList.Add(data);
                    }
                }
            }

            return healthDataList;
        }

        public List<HealthData> GetHealthDataBetween(DateTime start, DateTime end)
        {
            var list = new List<HealthData>();
            string query = @"SELECT [Date], [Shift], [HealthCategory], [StartTime], [EndTime], [TotalTime]
                             FROM [TEAL_Master].[dbo].[HealthData]
                             WHERE [StartTime] < @End AND [EndTime] > @Start
                             ORDER BY [StartTime]";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.Add("@Start", SqlDbType.DateTime).Value = start;
                command.Parameters.Add("@End", SqlDbType.DateTime).Value = end;

                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new HealthData
                        {
                            Date = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                            Shift = reader.IsDBNull(1) ? null : reader.GetString(1),
                            HealthCategory = reader.IsDBNull(2) ? null : reader.GetString(2),
                            StartTime = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3),
                            EndTime = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4),
                            TotalTime = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5)
                        });
                    }
                }
            }

            return list;
        }

        public void AddProductionData(ProductionData productionData)
        {
            string query = @"INSERT INTO [TEAL_Master].[dbo].[ProductionData]
           ([Date] ,[Shift] ,[ProductionTime] ,[ProductCode] ,[ProductionStatus] ,[CycleTime] ,[Iteration] ,[Tossing])
                VALUES
           ( @Date, @Shift, @ProductionTime, @ProductCode, @ProductionStatus, @CycleTime, @Iteration, @Tossing)";


            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", productionData.Date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@Shift", productionData.Shift);
                command.Parameters.AddWithValue("@ProductionTime", productionData.ProductionTime.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@ProductCode", productionData.ProductCode);
                command.Parameters.AddWithValue("@ProductionStatus", productionData.ProductionStatus ? 1 : 0);
                command.Parameters.AddWithValue("@CycleTime", productionData.CycleTime);
                command.Parameters.AddWithValue("@Iteration", productionData.Iteration);
                command.Parameters.AddWithValue("@Tossing", productionData.Tossing ? 1 : 0);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public ProductionData GetProductionData(string productCode)
        {
            ProductionData productionData = null;

            string query = @"SELECT [Date] ,[Shift] ,[ProductionTime] ,[ProductCode] ,[ProductionStatus] ,[CycleTime] ,[Iteration]
                             FROM [TEAL_Master].[dbo].[ProductionData] Where [ProductCode] = @ProductCode";

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ProductCode", productCode);
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            productionData = new ProductionData
                            {
                                Date = reader.GetDateTime(1),
                                Shift = reader.GetString(2),
                                ProductionTime = reader.GetDateTime(3),
                                ProductCode = reader.GetString(4),
                                ProductionStatus = reader.GetBoolean(5),
                                CycleTime = reader.GetInt32(6),
                                Iteration = reader.GetInt32(7),
                            };
                        }

                    }
                }
            }
            catch { }

            return productionData;
        }

        public void UpdateProductionData(String productCode, bool productionStatus, int iteration)
        {
            string query = @"UPDATE [TEAL_Master].[dbo].[ProductionData]
                             SET [ProductionStatus] = @ProductionStatus,
                                ,[Iteration] = @Iteration
                             WHERE [ProductCode] = @ProductCode";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@ProductionStatus", productionStatus);
                command.Parameters.AddWithValue("@Iteration", iteration);
                command.Parameters.AddWithValue("@ProductCode", productCode);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public void AddShiftData(ShiftData shiftData)
        {
            string query = @"INSERT INTO [TEAL_Master].[dbo].[ShiftData]
           ([Date] ,[Shift])
                VALUES
           (@Date, @Shift)";


            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", shiftData.Date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@Shift", shiftData.Shift);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public void UpdateShiftPlannedDowntime(DateTime date, string shift, int plannedDownTime, int breakTime)
        {
            string query = @"UPDATE [TEAL_Master].[dbo].[ShiftData]
                             SET [PlannedDownTime] = @PlannedDownTime,
                                 [BreakTime] = @BreakTime,
                                 [LastUpdated] = @LastUpdated   
                             WHERE [Date] = @Date and [Shift]=@Shift";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", date);
                command.Parameters.AddWithValue("@Shift", shift);
                command.Parameters.AddWithValue("@PlannedDownTime", plannedDownTime);
                command.Parameters.AddWithValue("@BreakTime", breakTime);
                command.Parameters.AddWithValue("@LastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public void UpdateShiftData(ShiftData shiftData)
        {
            string query = @"UPDATE [TEAL_Master].[dbo].[ShiftData]
                             SET [PassCount] = @PassCount,
                                 [FailCount] = @FailCount,
                                 [Yield] = @Yield,
                                 [OEE] = @OEE,
                                 [MTBF] = @MTBF,
                                 [MTBA] = @MTBA,
                                 [TossingRate] = @TossingRate,
                                 [RetestRate] = @RetestRate,
                                 [RunTime] = @RunTime, 
                                 [DownTime] = @DownTime, 
                                 [IdleTime] = @IdleTime,
                                 [LastUpdated] = @LastUpdated 
                             WHERE [Date] = @Date and [Shift]=@Shift";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", shiftData.Date);
                command.Parameters.AddWithValue("@Shift", shiftData.Shift);
                command.Parameters.AddWithValue("@PassCount", shiftData.PassCount);
                command.Parameters.AddWithValue("@FailCount", shiftData.FailCount);
                command.Parameters.AddWithValue("@Yield", shiftData.Yield);
                command.Parameters.AddWithValue("@OEE", shiftData.OEE);
                command.Parameters.AddWithValue("@MTBF", shiftData.MTBF);
                command.Parameters.AddWithValue("@MTBA", shiftData.MTBA);
                command.Parameters.AddWithValue("@TossingRate", shiftData.TossingRate);
                command.Parameters.AddWithValue("@RetestRate", shiftData.RetestRate);
                command.Parameters.AddWithValue("@RunTime", shiftData.RunTime);
                command.Parameters.AddWithValue("@DownTime", shiftData.DownTime);
                command.Parameters.AddWithValue("@IdleTime", shiftData.IdleTime);
                command.Parameters.AddWithValue("@LastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public ShiftData GetShiftData(DateTime date, string shift)
        {
            ShiftData shiftData = null;

            string query = @"SELECT [Date], [Shift],[RunTime],[DownTime],[IdleTime],[PlannedDownTime],[BreakTime],[PassCount],[FailCount],[Yield],[OEE],[MTBF],[MTBA],[TossingRate],[RetestRate], [LastUpdated]
                             FROM [TEAL_Master].[dbo].[ShiftData]
                             Where [Date] = @DateTime AND [Shift] = @Shift";
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@DateTime", date.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@Shift", shift);
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            shiftData = new ShiftData
                            {
                                Date = reader.IsDBNull(reader.GetOrdinal("Date")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Date")),
                                Shift = reader.IsDBNull(reader.GetOrdinal("Shift")) ? string.Empty : reader.GetString(reader.GetOrdinal("Shift")),
                                RunTime = reader.IsDBNull(reader.GetOrdinal("RunTime")) ? 0 : reader.GetDouble(reader.GetOrdinal("RunTime")),
                                DownTime = reader.IsDBNull(reader.GetOrdinal("DownTime")) ? 0 : reader.GetDouble(reader.GetOrdinal("DownTime")),
                                IdleTime = reader.IsDBNull(reader.GetOrdinal("IdleTime")) ? 0 : reader.GetDouble(reader.GetOrdinal("IdleTime")),
                                PlannedDownTime = reader.IsDBNull(reader.GetOrdinal("PlannedDownTime")) ? 0 : reader.GetDouble(reader.GetOrdinal("PlannedDownTime")),
                                BreakTime = reader.IsDBNull(reader.GetOrdinal("BreakTime")) ? 0 : reader.GetDouble(reader.GetOrdinal("BreakTime")),
                                PassCount = reader.IsDBNull(reader.GetOrdinal("PassCount")) ? 0 : reader.GetInt32(reader.GetOrdinal("PassCount")),
                                FailCount = reader.IsDBNull(reader.GetOrdinal("FailCount")) ? 0 : reader.GetInt32(reader.GetOrdinal("FailCount")),
                                Yield = reader.IsDBNull(reader.GetOrdinal("Yield")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("Yield")),
                                OEE = reader.IsDBNull(reader.GetOrdinal("OEE")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("OEE")),
                                MTBF = reader.IsDBNull(reader.GetOrdinal("MTBF")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("MTBF")),
                                MTBA = reader.IsDBNull(reader.GetOrdinal("MTBA")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("MTBA")),
                                TossingRate = reader.IsDBNull(reader.GetOrdinal("TossingRate")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("TossingRate")),
                                RetestRate = reader.IsDBNull(reader.GetOrdinal("RetestRate")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("RetestRate")),
                                LastUpdated = reader.IsDBNull(reader.GetOrdinal("LastUpdated")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("LastUpdated"))
                            };
                        }
                    }
                }
            }
            catch { }

            return shiftData;
        }

        public void AddOEEData(OEEData oEEData)
        {
            string query = @"INSERT INTO [TEAL_Master].[dbo].[OEEData]
                        ([DateTime], [Availability] ,[Performance] ,[Quality])
                            VALUES
                        (@DateTime, @Availability, @Performance, @Quality)";


            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@DateTime", oEEData.DateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@Availability", oEEData.Availability);
                command.Parameters.AddWithValue("@Performance", oEEData.Performance);
                command.Parameters.AddWithValue("@Quality", oEEData.Quality);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public List<OEEData> GetOEEData(DateTime startTime, DateTime endTime)
        {
            List<OEEData> oEEDataList = new List<OEEData>();
            string cmdText = @"SELECT [DateTime], [Availability], [Performance], [Quality] FROM [TEAL_Master].[dbo].[OEEData]
                               WHERE [DateTime]  >= '@StartTime' AND   [DateTime]  <= @EndTime";

            using (SqlConnection sqlConnection = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(cmdText, sqlConnection))
            {
                sqlConnection.Open();
                cmd.Parameters.AddWithValue("@StartTime", startTime);
                cmd.Parameters.AddWithValue("@EndTime", endTime);
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                    {
                        oEEDataList.Add(new OEEData
                        {
                            DateTime = reader.GetDateTime(1),
                            Availability = reader.GetDouble(2),
                            Performance = reader.GetDouble(3),
                            Quality = reader.GetDouble(3)
                        });
                    }
            }
            return oEEDataList;
        }

        public void AddProductionDataHourly(ProductionDataHourly productionDataHourly)
        {
            string query = @"INSERT INTO [TEAL_Master].[dbo].[ProductionDataHourly]
                        ([Date] ,[Shift] ,[Hour] ,[ProductCount])
                            VALUES
                        (@Date, @Shift, @Hour, @ProductCount)";


            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", productionDataHourly.Date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@Shift", productionDataHourly.Shift);
                command.Parameters.AddWithValue("@Hour", productionDataHourly.Hour);
                command.Parameters.AddWithValue("@ProductCount", productionDataHourly.ProductCount);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }
        public List<ProductionDataHourly> GetProductionDataHourly(DateTime date, string shift)
        {
            List<ProductionDataHourly> hourlyProductionDataList = new List<ProductionDataHourly>();

            string query = @"SELECT [Date], [Shift], [Hour], [ProductCount]
                            FROM [TEAL_Master].[dbo].[ProductionDataHourly]
                            HERE [Date] = @Date AND [Shift] = @Shift
                            ORDER BY [Hour]";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Date", date.Date.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@Shift", shift);

                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        hourlyProductionDataList.Add(new ProductionDataHourly
                        {
                            Date = Convert.ToDateTime(reader["Date"]),
                            Shift = reader["Shift"].ToString(),
                            Hour = Convert.ToInt32(reader["Hour"]),
                            ProductCount = Convert.ToInt32(reader["ProductCount"])
                        });
                    }
                }
            }

            return hourlyProductionDataList;
        }

        public int GetProductionCountByHour(DateTime startTime, DateTime endTime)
        {
            int count = 0;
            string query = @"SELECT COUNT(*)
            FROM [TEAL_Master].[dbo].[ProductionData]
             WHERE ProductionTime >= @StartTime
             AND ProductionTime < @EndTime";
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StartTime", startTime);
                    cmd.Parameters.AddWithValue("@EndTime", endTime);
                    conn.Open();
                    count = (int)cmd.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                // log exception
            }

            return count;
        }


        public List<AlarmData> GetAlarms(DateTime startTime, DateTime endTime)
        {
            List<AlarmData> alarmDatas = new List<AlarmData>();
            string cmdText = @"SELECT [AlarmCode],[AlarmDescription], [AlarmType], [Shift], [StartTime], [EndTime]
                               FROM [TEAL_Master].[dbo].[Alarms]  
                               WHERE [StartTime] >= @startTime AND [EndTime] <= @endTime";

            using (SqlConnection sqlConnection = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(cmdText, sqlConnection))
            {
                sqlConnection.Open();
                cmd.Parameters.AddWithValue("@StartTime", startTime);
                cmd.Parameters.AddWithValue("@EndTime", endTime);
                using (SqlDataReader reader = cmd.ExecuteReader())
                    while (reader.Read())
                    {
                        alarmDatas.Add(new AlarmData
                        {
                            AlarmCode = reader.IsDBNull(reader.GetOrdinal("AlarmCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("AlarmCode")),
                            AlarmDescription = reader.IsDBNull(reader.GetOrdinal("AlarmDescription")) ? string.Empty : reader.GetString(reader.GetOrdinal("AlarmDescription")),
                            AlarmType = reader.IsDBNull(reader.GetOrdinal("AlarmType")) ? string.Empty : reader.GetString(reader.GetOrdinal("AlarmType")),
                            Shift = reader.IsDBNull(reader.GetOrdinal("Shift")) ? string.Empty : reader.GetString(reader.GetOrdinal("Shift")),
                            StartTime = reader.IsDBNull(reader.GetOrdinal("StartTime")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("StartTime")),
                            EndTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("EndTime"))
                        });
                    }
            }
            return alarmDatas;
        }

        public void UpdateShiftMasterData(List<Shift> shifts)
        {
            throw new NotImplementedException();
        }

        public List<ProductionData> GetProductionData(DateTime startTime, DateTime endTime)
        {
            List<ProductionData> productionList = new List<ProductionData>();

            string query = @"SELECT [Date], [Shift], [ProductionTime], [ProductCode], [ProductionStatus], [CycleTime], [Iteration], [Tossing]
                            FROM [TEAL_Master].[dbo].[ProductionData]
                            WHERE [ProductionTime] >= @StartTime AND [ProductionTime] < @EndTime";

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("yyyy-MM-dd  HH:mm:ss"));
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ProductionData productionData = new ProductionData
                            {
                                Date = reader.IsDBNull(reader.GetOrdinal("Date")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Date")),
                                Shift = reader.IsDBNull(reader.GetOrdinal("Shift")) ? string.Empty : reader.GetString(reader.GetOrdinal("Shift")),
                                ProductionTime = reader.IsDBNull(reader.GetOrdinal("ProductionTime")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("ProductionTime")),
                                ProductCode = reader.IsDBNull(reader.GetOrdinal("ProductCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("ProductCode")),
                                ProductionStatus = reader.IsDBNull(reader.GetOrdinal("ProductionStatus")) ? false : reader.GetBoolean(reader.GetOrdinal("ProductionStatus")),
                                CycleTime = reader.IsDBNull(reader.GetOrdinal("CycleTime")) ? 0 : reader.GetDouble(reader.GetOrdinal("CycleTime")),
                                Iteration = reader.IsDBNull(reader.GetOrdinal("Iteration")) ? 0 : reader.GetInt32(reader.GetOrdinal("Iteration")),
                                Tossing = !reader.IsDBNull(reader.GetOrdinal("Tossing")) && reader.GetBoolean(reader.GetOrdinal("Tossing"))
                            };

                            productionList.Add(productionData);
                        }
                    }
                }
            }
            catch (Exception ex) { }
            return productionList;
        }

        public void DeleteHealthDataBefore(DateTime retentionDate)
        {
            string query = @"DELETE FROM [TEAL_Master].[dbo].[HealthData]
                     WHERE [Date] < @RetentionDate";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RetentionDate", retentionDate.Date.ToString("yyyy-MM-dd"));

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteProductionDataBefore(DateTime retentionDate)
        {
            string query = @"DELETE FROM [TEAL_Master].[dbo].[ProductionData]
                             WHERE [Date] < @RetentionDate";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RetentionDate", retentionDate.Date.ToString("yyyy-MM-dd"));

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
        public void DeleteShiftStatusDataBefore(DateTime retentionDate)
        {
            string query = @"DELETE FROM [TEAL_Master].[dbo].[ShiftData]
                             WHERE [Date] < @RetentionDate";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RetentionDate", retentionDate.Date.ToString("yyyy-MM-dd"));

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
        public void DeleteAlarmDataBefore(DateTime retentionDate)
        {
            string query = @"DELETE FROM [TEAL_Master].[dbo].[Alarms]
                     WHERE [EndTime] < @RetentionDate";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RetentionDate", retentionDate.Date.ToString("yyyy-MM-dd HH:mm:ss"));

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
        public void DeleteOEEDataBefore(DateTime retentionDate)
        {
            string query = @"DELETE FROM [TEAL_Master].[dbo].[OEEData]
                     WHERE [DateTime] <= @RetentionDate";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RetentionDate", retentionDate.Date.ToString("yyyy-MM-dd"));

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
        public void InsertAlarmQuery(string _shift, string _smsg, string _command, DateTime _starttime)
        {
            string query = @"INSERT INTO [TEAL_Master].[dbo].[Alarms] 
           ([Shift] ,[AlarmType] ,[AlarmCode] ,[AlarmDescription] ,[StartTime] ,[EndTime])
                VALUES
           ( @Shift, @AlarmType, @AlarmCode, @AlarmDescription, @StartTime, @EndTime)";


            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Shift", _shift);
                command.Parameters.AddWithValue("@AlarmType", "Critical");
                //command.Parameters.AddWithValue("@ErrorCode", new SqlParameter("@ErrorCode", _command));
                command.Parameters.AddWithValue("@AlarmCode", _command);
                command.Parameters.AddWithValue("@AlarmDescription", _smsg);
                command.Parameters.AddWithValue("@StartTime", _starttime);//.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@EndTime", DateTime.Now);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }
        public int InsertAlarm(string shift, string alarmType, string alarmCode, string alarmDescription, DateTime startTime, DateTime? endTime)
        {
            const string query = @"
                INSERT INTO [TEAL_Master].[dbo].[Alarms] ([AlarmCode], [AlarmType], [AlarmDescription], [Shift], [StartTime], [EndTime])
                VALUES (@AlarmCode, @AlarmType, @AlarmDescription, @Shift, @StartTime, @EndTime);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@AlarmCode", alarmCode ?? string.Empty);
                command.Parameters.AddWithValue("@AlarmType", alarmType ?? string.Empty);
                command.Parameters.AddWithValue("@AlarmDescription", alarmDescription ?? string.Empty);
                command.Parameters.AddWithValue("@Shift", shift ?? string.Empty);
                command.Parameters.AddWithValue("@StartTime", startTime);
                command.Parameters.AddWithValue("@EndTime", endTime.HasValue ? (object)endTime.Value : DBNull.Value);

                connection.Open();
                return (int)command.ExecuteScalar();
            }
        }

        public void UpdateAlarmEndTime(int alarmId, DateTime endTime)
        {
            const string query = "UPDATE [TEAL_Master].[dbo].[Alarms] SET [EndTime] = @EndTime WHERE [ID] = @ID";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@EndTime", endTime);
                command.Parameters.AddWithValue("@ID", alarmId);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public List<OpenAlarm> GetOpenAlarms()
        {
            const string query = @"
                SELECT [ID], [AlarmCode], [AlarmType], [AlarmDescription], [StartTime]
                FROM [TEAL_Master].[dbo].[Alarms]
                WHERE [EndTime] IS NULL AND [StartTime] IS NOT NULL";

            var alarms = new List<OpenAlarm>();

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        alarms.Add(new OpenAlarm
                        {
                            Id = (int)reader["ID"],
                            AlarmCode = reader["AlarmCode"] as string ?? string.Empty,
                            AlarmType = reader["AlarmType"] as string ?? string.Empty,
                            AlarmDescription = reader["AlarmDescription"] as string ?? string.Empty,
                            StartTime = (DateTime)reader["StartTime"],
                        });
                    }
                }
            }

            return alarms;
        }

        public DataTable AlarmviewQuery(string _shift, DateTime Starttime, DateTime Endtime)
        {
            DataTable dt = new DataTable();

            // The two branches were the wrong way round: asking for "All" applied
            // WHERE [SHIFT] = 'All' (no row carries that value, so nothing came back),
            // while asking for a real shift applied no shift predicate at all and
            // returned every shift. The caller passes null for "all shifts" and a shift
            // name otherwise, so only the second case was reachable — the Alarm screen's
            // SHIFT selection was silently ignored.
            //
            // Semantics now match CsvDataAccess.AlarmviewQuery: null, empty or "All"
            // means every shift; anything else filters on it.
            bool allShifts = string.IsNullOrWhiteSpace(_shift) ||
                             _shift.Equals("All", StringComparison.OrdinalIgnoreCase);

            string SqlQuery;
            if (allShifts)
            {
                SqlQuery = @"
                    SELECT *
                    FROM [TEAL_Master].[dbo].[Alarms]
                    WHERE [STARTTIME] BETWEEN @StartTime AND @EndTime
                    ORDER BY [STARTTIME] ASC";
            }
            else
            {
                SqlQuery = @"
                    SELECT *
                    FROM [TEAL_Master].[dbo].[Alarms]
                    WHERE [SHIFT] = @Shift
                    AND [STARTTIME] BETWEEN @StartTime AND @EndTime
                    ORDER BY [STARTTIME] ASC";
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(SqlQuery, connection))
            {
                // Added only when the query references it. It used to be added
                // unconditionally with a possibly-null value, which ADO.NET treats as
                // "not supplied" — harmless only because the query that referenced it
                // was unreachable.
                if (!allShifts)
                {
                    command.Parameters.AddWithValue("@Shift", _shift);
                }

                command.Parameters.AddWithValue("@StartTime", Starttime);
                command.Parameters.AddWithValue("@EndTime", Endtime);

                connection.Open();

                using (SqlDataAdapter da = new SqlDataAdapter(command))
                {
                    da.Fill(dt);
                }
                return dt;
            }
        }

        public int GetPassCountByHour(DateTime startTime, DateTime endTime)
        {
            int count = 0;

            string query = @"
                    SELECT COUNT(*)
                    FROM [TEAL_Master].[dbo].[ProductionData]
                    WHERE ProductionTime >= @StartTime
                      AND ProductionTime < @EndTime
                      AND ProductionStatus = 1";

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StartTime", startTime);
                    cmd.Parameters.AddWithValue("@EndTime", endTime);

                    conn.Open();
                    count = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                // Log Exception
            }

            return count;
        }
        //fail
        public int GetFailCountByHour(DateTime startTime, DateTime endTime)
        {
            int count = 0;

            string query = @"
                            SELECT COUNT(*)
                            FROM [TEAL_Master].[dbo].[ProductionData]
                            WHERE ProductionTime >= @StartTime
                              AND ProductionTime < @EndTime
                              AND ProductionStatus = 0";

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StartTime", startTime);
                    cmd.Parameters.AddWithValue("@EndTime", endTime);

                    conn.Open();
                    count = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                // Log Exception
            }

            return count;
        }
        /// <summary>
        /// Reports -> Machine Status. Same stored procedure and the same three
        /// parameters V3's Report_Screen.btnloadMachineStatus_Click passed to
        /// SqlUtility.ExecuteQueryAsync, including "ALL" as the every-shift value its
        /// combo offers. Both report methods used to throw NotImplementedException, so
        /// the two tabs could only ever report a failure.
        /// </summary>
        public DataTable GetMachineStatusReport(string shift, DateTime startTime, DateTime endTime)
        {
            return ExecuteReportProcedure("SP_GetMachineStatusReport", shift, startTime, endTime);
        }

        /// <summary>Reports -> Machine Health, via SP_GetMachineHealthReport. See
        /// <see cref="GetMachineStatusReport"/>.</summary>
        public DataTable GetMachineHealthReport(string shift, DateTime startTime, DateTime endTime)
        {
            return ExecuteReportProcedure("SP_GetMachineHealthReport", shift, startTime, endTime);
        }

        /// <summary>
        /// Runs one of the report procedures and returns its result set as-is — the
        /// column list belongs to the procedure, and the caller binds it straight to a
        /// grid. Exceptions are left to propagate: the report screen shows the message,
        /// which is the only way an operator learns the procedure is missing.
        /// </summary>
        private DataTable ExecuteReportProcedure(string procedureName, string shift,
            DateTime startTime, DateTime endTime)
        {
            DataTable dt = new DataTable();

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(procedureName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                // Both procedures test the every-shift case as WHERE (@Shift = 'All'
                // OR SHIFT = @Shift), so the sentinel has to reach them spelled exactly
                // that way. The report screen sends "ALL", which only matches under a
                // case-insensitive collation; normalise it here so it matches under any.
                bool allShifts = string.IsNullOrWhiteSpace(shift) ||
                                 shift.Equals("All", StringComparison.OrdinalIgnoreCase);
                command.Parameters.AddWithValue("@Shift", allShifts ? "All" : shift);
                command.Parameters.AddWithValue("@StartTime", startTime);
                command.Parameters.AddWithValue("@EndTime", endTime);

                connection.Open();
                using (SqlDataAdapter adapter = new SqlDataAdapter(command))
                {
                    adapter.Fill(dt);
                }
            }

            return dt;
        }

        public DataTable GetProdData(string _shift, DateTime startTime, DateTime endTime, string barCode)
        {
            DataTable dt = new DataTable();
            try
            {
                
                string SqlQuery;
                if (_shift.ToUpper() == "ALL")
                {
                    SqlQuery = @"
                    SELECT *
                    FROM [TEAL_Master].[dbo].[ProductionData] 
                    [ProductCode] = @BardCode
                    AND [ProductionTime] BETWEEN @StartTime AND @EndTime
                    ORDER BY [ProductionTime] ASC";
                }
                else
                {
                    SqlQuery = @"
                    SELECT *
                    FROM [TEAL_Master].[dbo].[ProductionData]
                    WHERE [SHIFT] = @shift AND [ProductCode] = @BardCode
                    AND [ProductionTime] BETWEEN @StartTime AND @EndTime
                    ORDER BY [ProductionTime] ASC";
                }

                using (SqlConnection connection = new SqlConnection(_connectionString))
                using (SqlCommand command = new SqlCommand(SqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@Shift", _shift);
                    command.Parameters.AddWithValue("@BardCode", barCode);
                    command.Parameters.AddWithValue("@StartTime", startTime);
                    command.Parameters.AddWithValue("@EndTime", endTime);

                    connection.Open();

                    using (SqlDataAdapter da = new SqlDataAdapter(command))
                    {
                        da.Fill(dt);
                    }
                   
                }
            }
            catch(Exception ex)
            {
                // Log Exception
            }
            return dt;
        }

        public DataTable GetMachineHealth(string _shift, DateTime startTime, DateTime endTime, string Category)
        {
            DataTable dt = new DataTable();
            string SqlQuery;
            try
            {
                if (_shift.ToUpper() == "ALL")
                {
                    SqlQuery = @"
                    SELECT *
                    FROM [TEAL_Master].[dbo].[MachineHealth]                    
                    WHERE DateTime >= @StartTime
                    AND DateTime < DATEADD(DAY, 1, @EndTime)
                    ORDER BY DateTime DESC, Shift;";
                }
                else
                {
                    SqlQuery = @"
                    SELECT *
                    FROM [TEAL_Master].[dbo].[MachineHealth]
                    WHERE Shift = @Shift
                    AND DateTime >= @StartTime
                    AND DateTime < DATEADD(DAY, 1, @EndTime)
                    ORDER BY DateTime DESC, Shift;";
                }
                    

                using (SqlConnection connection = new SqlConnection(_connectionString))
                using (SqlCommand command = new SqlCommand(SqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@Shift", _shift);
                    command.Parameters.AddWithValue("@StartTime", startTime);
                    command.Parameters.AddWithValue("@EndTime", endTime);

                    connection.Open();

                    using (SqlDataAdapter da = new SqlDataAdapter(command))
                    {
                        da.Fill(dt);
                    }
                    
                }
            }
            catch (Exception ex)
            { 
                //Logs Exception
            }
            return dt;
        }

        public DataTable GetMachineStatus(string _shift, DateTime startTime, DateTime endTime)
        {
            DataTable dt = new DataTable();
            string SqlQuery;
            try
            {
                if (_shift.ToUpper() == "ALL")
                {
                    SqlQuery = @"
                SELECT
                ISNULL(SUM([PassCount]), 0) AS TotalPassCount,
                ISNULL(SUM([FailCount]), 0) AS TotalFailCount,
                ISNULL(SUM([OverallCount]), 0) AS TotalOverallCount,
                ISNULL(SUM([CycleTime]), 0) AS TotalCycleTime
                FROM [TEAL_Master].[dbo].[MachineStatus] WITH (NOLOCK)
                WHERE [Date] >= @StartTime
                AND [Date] < DATEADD(DAY, 1, @EndTime);";
                }
                else
                {
                    SqlQuery = @"
                SELECT
                ISNULL(SUM([PassCount]), 0) AS TotalPassCount,
                ISNULL(SUM([FailCount]), 0) AS TotalFailCount,
                ISNULL(SUM([OverallCount]), 0) AS TotalOverallCount,
                ISNULL(SUM([CycleTime]), 0) AS TotalCycleTime
                FROM [TEAL_Master].[dbo].[MachineStatus] WITH (NOLOCK)
                WHERE [Date] >= @StartTime
                AND [Date] < DATEADD(DAY, 1, @EndTime)
                AND [Shift] = @Shift;";

                }
                    

                using (SqlConnection connection = new SqlConnection(_connectionString))
                using (SqlCommand command = new SqlCommand(SqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@Shift", _shift);
                    command.Parameters.AddWithValue("@StartTime", startTime);
                    command.Parameters.AddWithValue("@EndTime", endTime);

                    connection.Open();

                    using (SqlDataAdapter da = new SqlDataAdapter(command))
                    {
                        da.Fill(dt);
                    }
                    
                }
            }
            catch (Exception ex)
            {
                //Logs Exception
            }
            return dt;

        }

        public DataTable GetMachineProdData(string _shift, DateTime startTime, DateTime endTime, string ProductCode)
        {
            DataTable dt = new DataTable();
            string SqlQuery;
            try
            {
                if (_shift.ToUpper() == "ALL")
                {
                    SqlQuery = @"
                SELECT *
                FROM [TEAL_Master].[dbo].[ProductionData] WITH (NOLOCK)
                WHERE ProductionTime >= @startTime
                AND ProductionTime < DATEADD(DAY, 1, @endTime)                
                AND ProductCode = @ProductCode
                ORDER BY ProductionTime DESC;";
                }
                else
                {
                    SqlQuery = @"
                SELECT *
                FROM [TEAL_Master].[dbo].[ProductionData] WITH (NOLOCK)
                WHERE ProductionTime >= @startTime
                AND ProductionTime < DATEADD(DAY, 1, @endTime)
                AND Shift = @Shift
                AND ProductCode = @ProductCode
                ORDER BY ProductionTime DESC;";
                }
                    

                using (SqlConnection connection = new SqlConnection(_connectionString))
                using (SqlCommand command = new SqlCommand(SqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@Shift", _shift);
                    command.Parameters.AddWithValue("@StartTime", startTime);
                    command.Parameters.AddWithValue("@EndTime", endTime);
                    command.Parameters.AddWithValue("@ProductCode", ProductCode);

                    connection.Open();

                    using (SqlDataAdapter da = new SqlDataAdapter(command))
                    {
                        da.Fill(dt);
                    }
                    
                }
            }
            catch (Exception ex)
            {
                //Logs Exception
            }
            return dt;

        }

    }
}

