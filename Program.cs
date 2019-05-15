using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp14
{
    class Program
    {
        public static long TotalMemory => GC.GetTotalMemory(false);

        public static string TotalKBString => string.Concat((TotalMemory / 1024).ToString("#,0"), " KB").PadLeft(12); 

        static bool IsExit { get; set; } = false;

        static void Main(string[] args)
        {
            Task.Run(new Action(Proc)).ConfigureAwait(false);
            Console.ReadLine();
            Console.WriteLine("終了処理中...");
            IsExit = true;
        }
        static void Proc()
        {
            // 同期ﾓｰﾄﾞとｼﾞｬｰﾅﾙﾓｰﾄﾞのﾊﾟﾀｰﾝ作成
            var syncs = new[]
            {
                SynchronizationModes.Full,
                SynchronizationModes.Normal,
                SynchronizationModes.Off
            };
            var journals = new[]
            {
                SQLiteJournalModeEnum.Default,
                SQLiteJournalModeEnum.Delete,
                SQLiteJournalModeEnum.Memory,
                SQLiteJournalModeEnum.Off,
                SQLiteJournalModeEnum.Persist,
                SQLiteJournalModeEnum.Truncate,
                SQLiteJournalModeEnum.Wal,
            };
            var patterns = syncs
                .SelectMany(s => journals.Select(j => new { s, j }))
                .ToArray();

            // ｽﾄｯﾌﾟｳｫｯﾁ
            var stopwatch = new Stopwatch();
            // ﾜｰｸﾃﾞｨﾚｸﾄﾘ
            var work = System.AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            // ﾛｸﾞﾌｧｲﾙ
            var logfile = Path.Combine(work, $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.log");

            while (!IsExit)
            {
                /* **************************************************
                 * 最後までｺﾈｸｼｮﾝは閉じないﾊﾟﾀｰﾝ
                 * **************************************************/
                foreach (var pattern in patterns)
                {
                    var sb = new StringBuilder();

                    sb.Append($"1\t{TotalKBString}\t{pattern.s}\t{pattern.j}");
                    Console.WriteLine($"1:{TotalKBString}\t{pattern.s} {pattern.j}");

                    var path = Path.Combine(work, System.Guid.NewGuid().ToString()+".db");
                    using (var conn = GetConnection(path, pattern.s, pattern.j))
                    using (var command = conn.CreateCommand())
                    {
                        // ﾃｰﾌﾞﾙ作成
                        Create(command);

                        stopwatch.Restart();
                        // ﾃﾞｰﾀｲﾝｻｰﾄ(1000件単位でｺﾐｯﾄ)
                        for (var i = 0; i < 1000; i++)
                        {
                            Insert(command);
                        }
                        sb.Append($"\tINSERT: {stopwatch.Elapsed}");

                        stopwatch.Restart();
                        // ﾃﾞｰﾀ取得(1000回分)
                        for (var i = 0; i < 10; i++)
                        {
                            Select(command);
                        }
                        sb.Append($"\tSELECT: {stopwatch.Elapsed}");
                        sb.AppendLine();
                    }

                    File.AppendAllText(logfile, sb.ToString());
                    File.Delete(path);
                }

                /* **************************************************
                 * 逐次ｺﾈｸｼｮﾝをｸﾛｰｽﾞするﾊﾟﾀｰﾝ
                 * **************************************************/
                foreach (var pattern in patterns)
                {
                    var sb = new StringBuilder();

                    sb.Append($"2\t{TotalKBString}\t{pattern.s}\t{pattern.j}");
                    Console.WriteLine($"2:{TotalKBString}\t{pattern.s} {pattern.j}");

                    var path = Path.Combine(work, System.Guid.NewGuid().ToString() + ".db");
                    using (var conn = GetConnection(path, pattern.s, pattern.j))
                    using (var command = conn.CreateCommand())
                    {
                        // ﾃｰﾌﾞﾙ作成
                        Create(command);
                    }

                    stopwatch.Restart();
                    // ﾃﾞｰﾀｲﾝｻｰﾄ(1000件単位でｺﾐｯﾄ)
                    for (var i = 0; i < 1000; i++)
                    {
                        using (var conn = GetConnection(path, pattern.s, pattern.j))
                        using (var command = conn.CreateCommand())
                        {
                            Insert(command);
                        }
                    }
                    sb.Append($"\tINSERT: {stopwatch.Elapsed}");

                    using (var conn = GetConnection(path, pattern.s, pattern.j))
                    using (var command = conn.CreateCommand())
                    {
                        stopwatch.Restart();
                        // ﾃﾞｰﾀ取得(1000回分)
                        for (var i = 0; i < 10; i++)
                        {
                            Select(command);
                        }
                        sb.Append($"\tSELECT: {stopwatch.Elapsed}");
                        sb.AppendLine();
                    }

                    File.AppendAllText(logfile, sb.ToString());
                    File.Delete(path);
                }
            }
        }

        static SQLiteConnection GetConnection(string path, SynchronizationModes sync, SQLiteJournalModeEnum jornal)
        {
            var connectionString = new SQLiteConnectionStringBuilder()
            {
                DataSource = path,
                DefaultIsolationLevel = System.Data.IsolationLevel.ReadCommitted,
                SyncMode = sync,
                JournalMode = jornal,
                Pooling = false,
                CacheSize = 65535
            };

            var conn = new SQLiteConnection(connectionString.ToString());
            conn.Open();

            return conn;
        }

        static void Execute(SQLiteCommand command, string sql)
        {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        static void Create(SQLiteCommand command)
        {
            Execute(command, "CREATE TABLE test_table (id INTEGER PRIMARY KEY AUTOINCREMENT, message TEXT)");
        }

        static void Insert(SQLiteCommand command)
        {
            Execute(command, $"BEGIN");
            for (var j = 0; j < 1000; j++)
            {
                Execute(command, $"INSERT INTO test_table (message) VALUES ('{System.Guid.NewGuid().ToString()}')");
            }
            Execute(command, $"COMMIT");
        }

        static void Select(SQLiteCommand command)
        {
            command.CommandText = $"SELECT * FROM test_table";
            var sb = new StringBuilder();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    sb.AppendLine($"{reader.GetInt32(0)} {reader.GetString(1)}");
                }
            }
        }
    }
}
