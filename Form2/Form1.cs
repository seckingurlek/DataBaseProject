using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Form2
{
    public partial class Form1 : Form
    {
        private const string ReadCommitted = "ReadCommitted";
        private const string ReadUncommitted = "ReadUncommitted";
        private const string RepeatableRead = "RepeatableRead";
        private const string Serializable = "Serializable";

        static readonly string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=AdventureWorks2019;Integrated Security=True;Trust Server Certificate=True";
        static readonly string detailTable = "Sales.SalesOrderDetail";
        static readonly string headerTable = "Sales.SalesOrderHeader";

        private static int writerDeadlockCount = 0; // Shared writer deadlock counter
        private static int readerDeadlockCount = 0; // Shared reader deadlock counter

        public Form1()
        {
            InitializeComponent();
        }

        private void start_btn_Click(object sender, EventArgs e)
        {
            string isolationLevel = this.isolationLevel.Text;
            int writerCount = (int)typeAUser.Value;
            int readerCount = (int)typeBUser.Value;

            string selectedIsolationLevel = isolationLevel switch
            {
                ReadCommitted => "READ COMMITTED",
                ReadUncommitted => "READ UNCOMMITTED",
                RepeatableRead => "REPEATABLE READ",
                Serializable => "SERIALIZABLE",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(selectedIsolationLevel))
            {
                MessageBox.Show("Please select a valid isolation level.");
                return;
            }

            Console.WriteLine($"Starting simulation for {selectedIsolationLevel} with {writerCount} writers and {readerCount} readers.");
            RunSimulation(selectedIsolationLevel, writerCount, readerCount);
            Console.WriteLine($"Simulation completed for {selectedIsolationLevel}.\nWriter deadlocks: {writerDeadlockCount}\nReader deadlocks: {readerDeadlockCount}");
        }

        static void RunSimulation(string isolationLevel, int writerCount, int readerCount)
        {
            writerDeadlockCount = 0; // Reset writer deadlock counter
            readerDeadlockCount = 0; // Reset reader deadlock counter
            Task[] tasks = new Task[writerCount + readerCount];

            // Start writer threads
            for (int i = 0; i < writerCount; i++)
            {
                int threadId = i + 1;
                tasks[i] = Task.Run(() => UpdateDatabase(isolationLevel, threadId));
            }

            // Start reader threads
            for (int i = writerCount; i < writerCount + readerCount; i++)
            {
                int threadId = i + 1;
                tasks[i] = Task.Run(() => ReadDatabase(isolationLevel, threadId));
            }

            Task.WhenAll(tasks).Wait();
        }

        static void UpdateDatabase(string isolationLevel, int threadId)
        {
            Random rand = new Random();
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SET TRANSACTION ISOLATION LEVEL {isolationLevel};";
                    command.ExecuteNonQuery();

                    var dateRanges = new List<(string, string)>
                    {
                        ("20110101", "20111231"),
                        ("20120101", "20121231"),
                        ("20130101", "20131231"),
                        ("20140101", "20141231"),
                        ("20150101", "20151231")
                    };

                    for (int i = 0; i < 100; i++)
                    {
                        try
                        {
                            foreach (var (beginDate, endDate) in dateRanges)
                            {
                                if (rand.NextDouble() < 0.5)
                                {
                                    command.CommandText = $@"
                                        UPDATE {detailTable}
                                        SET UnitPrice = UnitPrice * 10.0 / 10.0
                                        WHERE UnitPrice > 100
                                        AND EXISTS (
                                            SELECT * FROM {headerTable}
                                            WHERE {headerTable}.SalesOrderID = {detailTable}.SalesOrderID
                                            AND {headerTable}.OrderDate BETWEEN '{beginDate}' AND '{endDate}'
                                            AND {headerTable}.OnlineOrderFlag = 1
                                        );";
                                    command.ExecuteNonQuery();
                                    Console.WriteLine($"Update thread {threadId} ({isolationLevel}): ");
                                }
                            }
                        }
                        catch (SqlException ex)
                        {
                            if (ex.Number == 1205) // Deadlock error number in SQL Server
                            {
                                Interlocked.Increment(ref writerDeadlockCount);
                                Console.WriteLine($"Deadlock occurred in writer thread {threadId}");
                            }
                        }
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Update thread {threadId} ({isolationLevel}) completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
        }

        static void ReadDatabase(string isolationLevel, int threadId)
        {
            Random rand = new Random();
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SET TRANSACTION ISOLATION LEVEL {isolationLevel};";
                    command.ExecuteNonQuery();

                    var dateRanges = new List<(string, string)>
                    {
                        ("20110101", "20111231"),
                        ("20120101", "20121231"),
                        ("20130101", "20131231"),
                        ("20140101", "20141231"),
                        ("20150101", "20151231")
                    };

                    for (int i = 0; i < 100; i++)
                    {
                        try
                        {
                            foreach (var (beginDate, endDate) in dateRanges)
                            {
                                if (rand.NextDouble() < 0.5)
                                {
                                    command.CommandText = $@"
                                        SELECT SUM({detailTable}.OrderQty)
                                        FROM {detailTable}
                                        WHERE UnitPrice > 100
                                        AND EXISTS (
                                            SELECT * FROM {headerTable}
                                            WHERE {headerTable}.SalesOrderID = {detailTable}.SalesOrderID
                                            AND {headerTable}.OrderDate BETWEEN '{beginDate}' AND '{endDate}'
                                            AND {headerTable}.OnlineOrderFlag = 1
                                        );";
                                    using (var reader = command.ExecuteReader())
                                    {
                                        while (reader.Read())
                                        {
                                            var sumQty = reader[0] is DBNull ? 0 : reader.GetInt32(0);
                                            Console.WriteLine($"Read thread {threadId} ({isolationLevel}): OrderQty Sum = {sumQty}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (SqlException ex)
                        {
                            if (ex.Number == 1205) // Deadlock error number in SQL Server
                            {
                                Interlocked.Increment(ref readerDeadlockCount);
                                Console.WriteLine($"Deadlock occurred in reader thread {threadId}");
                            }
                        }
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Read thread {threadId} ({isolationLevel}) completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
        }

        private void Form1_Load(object sender, EventArgs e) { }

        private void typeBUser_ValueChanged(object sender, EventArgs e) { }

        private void typeAUser_ValueChanged(object sender, EventArgs e) { }

        private void isolationLevel_SelectedIndexChanged(object sender, EventArgs e) { }
    }
}
