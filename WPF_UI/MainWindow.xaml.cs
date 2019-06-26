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
using System.Net.Http;
using Newtonsoft.Json;
namespace WPF_UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DataTable _ImportedData;
        string _DatabaseSource;

        public MainWindow()
        {
            InitializeComponent();
            _ImportedData = new DataTable();
            CB_Csv.IsChecked = true;
            TXT_Box_CSV_Directory.Text = "C:/Users/Kola-Desktop/Downloads/uk-500/test.csv"; // temp only!
            Connect();
            
        }

        private void BTN_CSV_Browse_Click(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.Text = GetFileNameFromFileSelection(".csv");
           
        }
        private void BTN_Database_Browse_Click(object sender, RoutedEventArgs e)
        {
            TXT_Box_Database_Directory.Text = GetFileNameFromFileSelection(".db");
            _DatabaseSource = @"Data Source = " + TXT_Box_Database_Directory.Text + "; version=3; ";
        }

        private string GetFileNameFromFileSelection(string p_FileExtension)
        {
            OpenFileDialog file = new OpenFileDialog();
            file.Filter = "(*" + p_FileExtension + ")|*" + p_FileExtension;
            if (file.ShowDialog() == true)
            {
                BTN_Import.IsEnabled = true;
              
               
                return file.FileName;
            }
            return "";
        }

        private void BTN_Import_Click(object sender, RoutedEventArgs e)
        {
            if (CB_Csv.IsChecked == true)
            {
                ReadDataFromFileCSV();
                StoreDataToDatabase();
            }
            else if (CB_Database.IsChecked == true)
            {
                SetMostCommonEmailInUI();
            }
            
          
           
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
            SQLiteConnection.CreateFile("MyDatabase2.db");
            
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=MyDatabase2.db;Version=3;"))
            {
                try
                {
                    conn.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand(conn))
                    {
                        using (var transaction = conn.BeginTransaction())
                        {
                            string headers = "";
                            for (int i = 0; i < _ImportedData.Columns.Count; i++)
                            {
                                headers += ", [" + _ImportedData.Columns[i].ColumnName + "] text NOT NULL";
                            }
                            headers += ");";
                            cmd.CommandText = "CREATE TABLE [Table_2] (" +
                                "[Id] INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL" + headers;

                            cmd.ExecuteNonQuery();

                            string headerSeries = "";
                            for (int i = 0; i < _ImportedData.Columns.Count; i++)
                            {
                                headerSeries += _ImportedData.Columns[i].ColumnName + ",";
                            }
                            headerSeries = headerSeries.Remove(headerSeries.Length - 1);
                            
                            for (int i = 0; i < _ImportedData.Rows.Count; i++)
                            {
                                string rowSeries = "";
                                for (int j = 0; j < _ImportedData.Rows[i].ItemArray.Length; j++)
                                {
                                    rowSeries += "'" + _ImportedData.Rows[i].ItemArray.GetValue(j) + "'" + ",";
                                }
                                rowSeries = rowSeries.Remove(rowSeries.Length - 1);
                                
                                cmd.CommandText = "INSERT INTO [Table_2](" +
                                    headerSeries +
                                    ") VALUES (" +
                                    rowSeries + ")";
                                cmd.ExecuteNonQuery();
                            }
                            transaction.Commit();
                        }
                        //cmd.ExecuteNonQuery();


                        //foreach (DataRow row in _ImportedData.Rows)
                        //{
                        //    cmd.CommandText = "INSERT INTO DatabaseTest(Data1, Data2) " + "VALUES(@data1, @data2); ";
                        //    cmd.Parameters.AddWithValue("@data1", row["Data1"]);
                        //    cmd.Parameters.AddWithValue("@data2", row["Data2"]);
                        //    cmd.ExecuteNonQuery();

                        //}
                        //cmd.CommandText = "DELETE FROM Db;";
                        //cmd.ExecuteNonQuery();
                        //transaction.Commit();

                        //}

                    }
                    MessageBox.Show("Complete!");
                    conn.Close();

                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message);
                }
            }
        }
        private void ReadDataFromFileCSV()
        {
            try
            {
                using (StreamReader sr = new StreamReader(TXT_Box_CSV_Directory.Text))
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
                        string cleanHeaderColumn = headerColumn;
                        if (headerColumn.Contains('"'))
                        {
                            if (cleanHeaderColumn[0] == '"')
                                cleanHeaderColumn = cleanHeaderColumn.Remove(0, 1);
                            if (cleanHeaderColumn[cleanHeaderColumn.Length - 1] == '"')
                                cleanHeaderColumn = cleanHeaderColumn.Remove(cleanHeaderColumn.Length - 1);
                        }
                        _ImportedData.Columns.Add(cleanHeaderColumn);
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
                            string cleanValues = values[i];
                            if (cleanValues.Contains('"'))
                            {
                                if(cleanValues[0] == '"')
                                    cleanValues = cleanValues.Remove(0, 1);
                                if(cleanValues[cleanValues.Length - 1] == '"')
                                    cleanValues = cleanValues.Remove(cleanValues.Length - 1);
                            }
                            importedRow[i] = cleanValues;
                        }
                        _ImportedData.Rows.Add(importedRow);
                    }
                    TXT_Box_CSV_Directory.Text = string.Empty;
                    MessageBox.Show("Data loaded successfully!", "SUCCESS!");

                    dataGrid.DataContext = _ImportedData.DefaultView;
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }

        private void Connect()
        {
           // Ttest();

        }

        public async void Ttest()
        {
            HttpClient client = new HttpClient();
            var response = await client.GetStringAsync("https://api.postcodes.io/postcodes/BS50SR");
           
        }
       
        private void SetMostCommonEmailInUI()
        {
            Dictionary<string, int> commonMail = GetMostCommonEmailDomains();
            TXT_Block_MostCommonEmailDomain_Name_1.Text = commonMail.Keys.ElementAt(0);
            TXT_Block_MostCommonEmailDomain_Name_2.Text = commonMail.Keys.ElementAt(1);
            TXT_Block_MostCommonEmailDomain_Name_3.Text = commonMail.Keys.ElementAt(2);

            TXT_Block_MostCommonEmailDomain_Value_1.Text = commonMail.Values.ElementAt(0).ToString();
            TXT_Block_MostCommonEmailDomain_Value_2.Text = commonMail.Values.ElementAt(1).ToString();
            TXT_Block_MostCommonEmailDomain_Value_3.Text = commonMail.Values.ElementAt(2).ToString();
        }


        private Dictionary<string, int> GetMostCommonEmailDomains()
        {
            using (SQLiteConnection connection = new SQLiteConnection(_DatabaseSource))
            {
                try
                {
                    connection.Open();
                    using (SQLiteCommand command = new SQLiteCommand(connection))
                    {
                        try
                        {
                            command.CommandText = "SELECT email FROM Database_Test";
                            command.ExecuteNonQuery();
                            SQLiteDataReader reader = command.ExecuteReader();

                            /* Read data from Table */
                            Dictionary<string, int> mostCommonEmailDomains_Raw = new Dictionary<string, int>();
                            while (reader.Read())
                            {
                                string email = reader.GetString(0);
                                string emailDomain = email.Substring((email.IndexOf("@") + 1), email.Length - (email.IndexOf("@") + 1));

                                if (!mostCommonEmailDomains_Raw.ContainsKey(emailDomain))
                                {
                                    mostCommonEmailDomains_Raw.Add(emailDomain, 1);
                                }
                                else
                                {
                                    foreach (var key in mostCommonEmailDomains_Raw.Where(item => item.Key == emailDomain).Select(item => item.Key).ToList())
                                    {
                                        mostCommonEmailDomains_Raw[key] += 1;
                                    }
                                }
                            }

                            /* Extract the three most common domains */
                            Dictionary<string, int> mostThreeCommonEmailDomains = new Dictionary<string, int>();
                            int counter = 3;
                            foreach (KeyValuePair<string, int> item in mostCommonEmailDomains_Raw.OrderByDescending(key => key.Value))
                            {
                                counter--;
                                mostThreeCommonEmailDomains.Add(item.Key, item.Value);
                                if (counter < 1)
                                    break;
                            }

                            connection.Close();
                            return mostThreeCommonEmailDomains;
                        }
                        catch (Exception error)
                        {
                            MessageBox.Show(error.Message);
                            return null;
                        }
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message);
                    return null;
                }
            }
        }

        private static string[] ParseLine(string rowLine)
        {
            //string[] seps = { "\",\"" };
            string separator = rowLine.Replace("\",\"", "~");
            if (separator.Contains('~'))
            {
                return separator.Split('~');
            }
            else
            {
                return separator.Split(',');
            }
        }

        private void CB_Database_Checked(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.IsEnabled = false;
            BTN_CSV_Browse.IsEnabled = false;
            CB_Csv.IsChecked = false;


            TXT_Box_Database_Directory.IsEnabled = true;
            BTN_Database_Browse.IsEnabled = true;
            CB_Database.IsChecked = true;

        }

        private void CB_Csv_Checked(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.IsEnabled = true;
            BTN_CSV_Browse.IsEnabled = true;
            CB_Csv.IsChecked = true;


            TXT_Box_Database_Directory.IsEnabled = false;
            BTN_Database_Browse.IsEnabled = false;
            CB_Database.IsChecked = false;
          //  TXT_Box_Database_Directory.Text = string.Empty;
        }

      
    }
}
