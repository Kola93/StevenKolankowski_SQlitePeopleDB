using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32; // Includes OpenFileDialog
using System.IO; // Includes StreamReader
using System.Data; // includes DataTable
using System.Data.SQLite; // includes SQLite
using System.Data.SqlClient;
using SqliteWrapper;
using Microsoft.CSharp;

namespace WPF_UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DataTable _ImportedData;

        public MainWindow()
        {
            InitializeComponent();
            _ImportedData = new DataTable();
            TXT_Box_Directory.Text = "C:/Users/Kola-Desktop/Downloads/uk-500/test.csv"; // temp only!
        }

        private void BTN_Browse_Click(object sender, RoutedEventArgs e)
        {
            ProcessOpenFileDialog();
        }

        private void ProcessOpenFileDialog()
        {
            OpenFileDialog file = new OpenFileDialog();
            file.Filter = "CSV files (*.csv)|*.csv";
            if (file.ShowDialog() == true)
            {
                TXT_Box_Directory.Text = file.FileName;
            }
        }

        private void BTN_Import_Click(object sender, RoutedEventArgs e)
        {
            ReadDataFromFile();
            StoreDataToDatabase();
        }
        private void StoreDataToDatabase()
        {
            //string conString = @"Data Source = C:\Users\Kola-Desktop\Documents\Jobs\Fatshark\StevenKolankowski_Test\WPF_UI\Data\MyDatabase.db; version=3;";
            //string dbConnectionString = @"Data Source=MyDatabase.db;Version=3;";
            //SQLiteConnection sqlite_con = new SQLiteConnection(conString);
            //sqlite_con.Open();
            //string query = "select * from DatabaseTest;";
            //SQLiteCommand sqlite_cmd = new SQLiteCommand(query, sqlite_con);
            //SQLiteDataReader dr = sqlite_cmd.ExecuteReader();
            
            //while (dr.Read())
            //{
            //    MessageBox.Show(dr.GetString(1));
            //}

            string conString = @"Data Source = C:\Users\Kola-Desktop\Documents\Jobs\Fatshark\StevenKolankowski_Test\WPF_UI\Data\MyDatabase.db; version=3;";
            using (SQLiteConnection conn = new SQLiteConnection(conString))
            {
                try
                {
                    conn.Open();


                    using (SQLiteCommand cmd = new SQLiteCommand(conn))
                    {
                        using (var transaction = conn.BeginTransaction())
                        {
                            
                            //foreach (DataRow row in _ImportedData.Rows)
                            //{
                            //    cmd.CommandText = "INSERT INTO DatabaseTest(Data1, Data2) " + "VALUES(@data1, @data2); ";
                            //    cmd.Parameters.AddWithValue("@data1", row["Data1"]);
                            //    cmd.Parameters.AddWithValue("@data2", row["Data2"]);
                            //    cmd.ExecuteNonQuery();
                                
                            //}
                               cmd.CommandText = "DELETE FROM Db;";
                            cmd.ExecuteNonQuery();
                            transaction.Commit();
                           
                        }
                        MessageBox.Show("Complete!");                     
                    }
                    conn.Close();

                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message);
                }
            }
        }
        private void ReadDataFromFile()
        {
            try
            {
                using (StreamReader sr = new StreamReader(TXT_Box_Directory.Text))
                {
                    // Setup Header
                    string header = sr.ReadLine();
                    if (string.IsNullOrEmpty(header))
                    {
                        MessageBox.Show("No data in file");
                        return;
                    }
                    string[] headerColumns = ParseLine(header);
                    foreach (string headerColumn in headerColumns)
                    {
                        _ImportedData.Columns.Add(headerColumn);
                    }

                    // Process each line
                    while (!sr.EndOfStream)
                    {
                        string line = sr.ReadLine();
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        DataRow importedRow = _ImportedData.NewRow();
                        string[] values = ParseLine(line);
                        for (int i = 0; i < values.Count(); i++)
                        {
                            importedRow[i] = values[i];
                        }
                        _ImportedData.Rows.Add(importedRow);
                    }
                    TXT_Box_Directory.Text = string.Empty;
                    MessageBox.Show("Data loaded successfully!", "SUCCESS!");
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }

        private static string[] ParseLine(string rowLine)
        {
            return rowLine.Split(',');
        }
    }
}
