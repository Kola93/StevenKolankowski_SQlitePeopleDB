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
using System.Diagnostics; // Timer

namespace WPF_UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DataTable _Data;
        DataTable _DataCoordinates;
        string _CurrentDatabase_Source;
        string _CurrentDatabase_MainTableName;
        string _CurrentDatabase_CoordinatesTableName;
        int _NumberOfEmailRecords;
        Stopwatch _ProcessingDataTime;   

        public class ResponseObject
        {
            public int status;
            public Result[] result;
        }
        public class Result
        {
            public string query;
            public ResultData result;
        }        public class ResultData
        {
            public string postcode;
            public float longitude;
            public float latitude;
        }
        public MainWindow()
        {
            InitializeComponent();
            ResetToDefault();
           
            TXT_Box_CSV_Directory.Text = "C:/Users/Kola-Desktop/Downloads/uk-500/test.csv"; // temp only!
            _CurrentDatabase_CoordinatesTableName = "Coordinates";

            _Data = new DataTable();
            _DataCoordinates = new DataTable();
            _ProcessingDataTime = new Stopwatch();

            //CB_Csv.IsChecked = true;
        }

        #region Buttons
        private void BTN_CSV_Browse_Click(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.Text = GetFileNameFromFileSelection(".csv");

        }
        private void BTN_Database_Browse_Click(object sender, RoutedEventArgs e)
        {
            TXT_Box_Database_Directory.Text = GetFileNameFromFileSelection(".db");
            _CurrentDatabase_Source = @"Data Source = " + TXT_Box_Database_Directory.Text + "; version=3; ";
        }
        private void BTN_ProcessMostCommonEmailAddresses_Click(object sender, RoutedEventArgs e)
        {
            if (CheckEmailFieldsValidity() == true)
            {
                ListView_MostCommonEmails.Items.Clear();
                ExecuteMostCommonEmailInUI();
            }            
        }
        private void BTN_Import_Click(object sender, RoutedEventArgs e)
        {
            if (CB_Csv.IsChecked == true)
            {
                if (CheckSeparatorFieldValidity() == true)
                {
                    ReadDataFromFileCSV();
                    CreateNewDatabaseAndTable();
                    ShowProcessingDataTime();
                }
            }
            else if (CB_Database.IsChecked == true)
            {
                if (CheckDatabaseTableNameFieldValidity() == true)
                {
                    _CurrentDatabase_MainTableName = TXT_Box_Database_TableName.Text;
                    ReadDataFromDatabase();
                }
            }
            SetVisibility(Panel_MostCommonEmailAddresses, Visibility.Visible);
            SetVisibility(Panel_LargestNumberOfPeopleLivingClose, Visibility.Visible);
            SetActive(Panel_DirectorySelection, false);
        }
        private void BTN_DownloadData_Click(object sender, RoutedEventArgs e)
        {
            if (CheckPostcodeFieldValidity() == true)
            {
                CreateNewCoordinateTableInDatabase();
                CreateDataTable_Coordinates();
            }
                   
        }
        private void BTN_Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetToDefault();
        }
        #endregion Buttons

        #region UI_Interaction
        private void CB_Database_Checked(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.IsEnabled = false;
            BTN_CSV_Browse.IsEnabled = false;
            CB_Csv.IsChecked = false;
            TXT_Box_CSV_Separator.IsEnabled = false;

            TXT_Box_Database_Directory.IsEnabled = true;
            BTN_Database_Browse.IsEnabled = true;
            CB_Database.IsChecked = true;
            TXT_Box_Database_TableName.IsEnabled = true;
        }
        private void CB_Csv_Checked(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.IsEnabled = true;
            BTN_CSV_Browse.IsEnabled = true;
            CB_Csv.IsChecked = true;
            TXT_Box_CSV_Separator.IsEnabled = true;

            TXT_Box_Database_Directory.IsEnabled = false;
            BTN_Database_Browse.IsEnabled = false;
            CB_Database.IsChecked = false;
            TXT_Box_Database_TableName.IsEnabled = false;
        }
        private void SetActive(UIElement p_Element, bool p_Active)
        {
            p_Element.IsEnabled = p_Active;
        }
        private void SetVisibility(UIElement p_Element, Visibility p_NewVisibility)
        {
            p_Element.Visibility = p_NewVisibility;
        }
        #endregion UI_Interaction        

        #region Insert
        private void InsertRowsInDataTable(SQLiteDataReader reader, ref DataTable p_DataTable)
        {
            while (reader.Read())
            {
                DataRow newRow = p_DataTable.NewRow();
                for (int i = 1; i < reader.FieldCount; i++)
                {
                    string value = reader.GetString(i);
                    newRow[i - 1] = value;
                }
                p_DataTable.Rows.Add(newRow);
            }
        }
        private bool InsertRowInTable(SQLiteCommand cmd, string p_TableName, string headerSeries, string rowSeries)
        {
            try
            {
                cmd.CommandText = "INSERT INTO [" + p_TableName + "](" + headerSeries + ") VALUES (" + rowSeries + ")";
                cmd.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return false;
            }
            return true;
        }
        #endregion Insert
               
        #region Create
        private bool CreateTable(SQLiteCommand cmd, string p_TableName, string headers)
        {
            try
            {
                cmd.CommandText = "CREATE TABLE [" + p_TableName + "](" + "[Id] INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL" + headers;
                cmd.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return false;
            }
            return true;
        }
        private void CreateNewDatabaseAndTable()
        {
            _ProcessingDataTime.Start();
            SQLiteConnection.CreateFile(TXT_Box_CSV_NewDBName.Text);
            _CurrentDatabase_Source = @"Data Source = " + TXT_Box_CSV_NewDBName.Text + "; version=3; ";
            _CurrentDatabase_MainTableName = TXT_Box_CSV_NewMainTableName.Text;

            try
            {

                SQLiteConnection conn = new SQLiteConnection(_CurrentDatabase_Source);
                conn.Open();
                SQLiteCommand cmd = new SQLiteCommand(conn);

                var transaction = conn.BeginTransaction();

                /* Create table with values from imported .csv */
                string headers = "";
                for (int i = 0; i < _Data.Columns.Count; i++)
                {
                    headers += ", [" + _Data.Columns[i].ColumnName + "] text NOT NULL";
                }
                headers += ");";

                if (!CreateTable(cmd, _CurrentDatabase_MainTableName, headers))
                {
                    MessageBox.Show("Couldn't create table!", "Error!");
                    return;
                }

                /* Setup headers */
                string headerSeries = "";
                for (int i = 0; i < _Data.Columns.Count; i++)
                {
                    headerSeries += _Data.Columns[i].ColumnName + ",";
                }
                headerSeries = headerSeries.Remove(headerSeries.Length - 1);

                /* Setup rows */
                for (int i = 0; i < _Data.Rows.Count; i++)
                {
                    string rowSeries = "";
                    for (int j = 0; j < _Data.Rows[i].ItemArray.Length; j++)
                    {
                        rowSeries += "'" + _Data.Rows[i].ItemArray.GetValue(j) + "'" + ",";
                    }
                    rowSeries = rowSeries.Remove(rowSeries.Length - 1);

                    if (!InsertRowInTable(cmd, _CurrentDatabase_MainTableName, headerSeries, rowSeries))
                    {
                        MessageBox.Show("Couln't insert row in Table!", "Error!");
                        return;
                    }
                }
                transaction.Commit();
                _ProcessingDataTime.Stop();
                MessageBox.Show("Table Created!", "SUCCESS!");
                conn.Close();
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }

        }
        private void CreateNewCoordinateTableInDatabase()
        {
            try
            {
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);
                var transaction = connection.BeginTransaction();

                /* override existing table */
                DropTableIfExist(command, "DROP TABLE IF EXISTS " + _CurrentDatabase_CoordinatesTableName + ";");

                string headers = ", [postcode] text NOT NULL, [longitude] text NOT NULL, [latitude] text NOT NULL);";
                if (!CreateTable(command, _CurrentDatabase_CoordinatesTableName, headers))
                {
                    MessageBox.Show("Couldn't create " + _CurrentDatabase_CoordinatesTableName + " table!", "Error!");
                    transaction.Rollback();
                    connection.Close();
                    SQLiteConnection.ClearAllPools();
                    return;
                }
                transaction.Commit();
                connection.Close();
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }
        public void CreateDataTable_Coordinates()
        {
            _DataCoordinates = null;
            _DataCoordinates = new DataTable();
            /* Setup Headers */
            _DataCoordinates.Columns.Add("postcode");
            _DataCoordinates.Columns.Add("longitude");
            _DataCoordinates.Columns.Add("latitude");
            try
            {            
                List<string> postcodes = new List<string>();

                /* Setup Database connection */
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);
                var transaction = connection.BeginTransaction();

                SQLiteDataReader reader = GetColumnReader_SQLiteCommand(command, _CurrentDatabase_MainTableName, TXT_Box_PostcodeColumnName.Text);
                if (reader == null)
                {
                    MessageBox.Show("Couln't get column", "Error!");
                    return;
                }
                BTN_DownloadData.IsEnabled = false;
                BTN_Reset.IsEnabled = false;
                /* Add each postcode to a list */
                while (reader.Read())
                {
                    string value = reader.GetString(0);
                    postcodes.Add(value);
                }
                FetchDataFromAPIWithPostcodes(postcodes);                
                transaction.Commit();               
            }
            catch (Exception error)
            {
                BTN_DownloadData.IsEnabled = true;
                BTN_Reset.IsEnabled = true;
                MessageBox.Show(error.ToString());
            }
        }
        #endregion Create
        
        #region Read
        private void ReadDataFromDatabase()
        {
            try
            {
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);

                /* Setup SQLite Command */
                command.CommandText = "SELECT * FROM " + _CurrentDatabase_MainTableName;
                command.ExecuteNonQuery();
                SQLiteDataReader reader = command.ExecuteReader();

                /* Setup table name */
                _Data.TableName = _CurrentDatabase_MainTableName;

                /* Setup Headers */
                for (int i = 1; i < reader.FieldCount; i++)
                {
                    _Data.Columns.Add(reader.GetName(i));
                }

                /* Setup Rows */
                InsertRowsInDataTable(reader, ref _Data);

                /* Assign DataGrid Reference */
                dataGrid.DataContext = _Data.DefaultView;
                MessageBox.Show("Data Imported!", "SUCCESS!");

                //connection.Close();
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }
        private void ReadDataFromFileCSV()
        {
            try
            {
                StreamReader sr = new StreamReader(TXT_Box_CSV_Directory.Text);

                _ProcessingDataTime = Stopwatch.StartNew();
                /* Setup Header */
                string header = sr.ReadLine();
                if (string.IsNullOrEmpty(header))
                {
                    MessageBox.Show("No data in file");
                    //return;
                }
                string[] headerColumns = ParseLine(header);
                foreach (string headerColumn in headerColumns)
                {
                    string cleanHeaderColumn = RemoveSymbolsInBeginAndEnd(headerColumn, '"');
                    _Data.Columns.Add(cleanHeaderColumn);
                }

                ProcessStreamReader(sr);

                TXT_Box_CSV_Directory.Text = string.Empty;
                _ProcessingDataTime.Stop();
                MessageBox.Show("Data loaded successfully! \nCreating new table...", "SUCCESS!");

            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }
        }
        private void ProcessStreamReader(StreamReader p_StreamReader)
        {
            while (!p_StreamReader.EndOfStream)
            {
                string line = p_StreamReader.ReadLine();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                DataRow importedRow = _Data.NewRow();
                string[] values = ParseLine(line);
                for (int i = 0; i < values.Count(); i++)
                {
                    importedRow[i] = RemoveSymbolsInBeginAndEnd(values[i], '"');
                }
                _Data.Rows.Add(importedRow);
                dataGrid.DataContext = _Data.DefaultView;
                TXT_Block_NumberOfRecordsImported.Text = _Data.Rows.Count.ToString();
            }
        }
        private async void FetchDataFromAPIWithPostcodes(List<string> p_Postcodes)
        {
            HttpClient client = new HttpClient();
            /* Process request for coordinates to API */
            int PostCodeIndex = 0;
            while (PostCodeIndex < p_Postcodes.Count)
            {
                string json = "{\"postcodes\":[";
                // 100 is the max amount of postcodes that the API can handle
                for (int i = 0; i < 100; i++)
                {
                    // Break if the total number of postcodes is < 100
                    if (i == p_Postcodes.Count)
                    {
                        break;
                    }
                    json += "\"" + p_Postcodes[PostCodeIndex] + "\",";
                    PostCodeIndex++;
                }
                /* Remove last "," */
                json = json.Remove(json.Length - 1);
                json += "]}";

                /* Call API with list of postcodes */
                var response = await client.PostAsync("https://api.postcodes.io/postcodes", new StringContent(json, Encoding.UTF8, "application/json"));
                string responseObjectJson = await response.Content.ReadAsStringAsync();
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                var responseObject = JsonConvert.DeserializeObject<ResponseObject>(responseObjectJson, settings);

                int status = responseObject.status;
                if (status == 404)
                {
                    MessageBox.Show("Error 404: Couldn't get the list of coordinates from API", "Error!");
                    return;
                }

                /* Assign values to DataTable */
                for (int i = 0; i < responseObject.result.Length; i++)
                {
                    DataRow newRow = _DataCoordinates.NewRow();
                    newRow[0] = responseObject.result[i].query;
                    if (responseObject.result[i].result == null)
                    {
                        newRow[1] = "";
                        newRow[2] = "";
                    }
                    else
                    {
                        newRow[1] = responseObject.result[i].result.longitude;
                        newRow[2] = responseObject.result[i].result.latitude;
                    }
                    _DataCoordinates.Rows.Add(newRow);
                    DataGrid_Coordinates.DataContext = _DataCoordinates.DefaultView;
                }
            }
            MessageBox.Show("Fetching complete!","SUCCESS");
            BTN_Reset.IsEnabled = true;
        }
        #endregion Read

        #region Execute
        private void ExecuteMostCommonEmailInUI()
        {
            try
            {
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);

                SQLiteDataReader reader = GetColumnReader_SQLiteCommand(command, _CurrentDatabase_MainTableName, TXT_Box_EmailColumn.Text);
                if (reader == null)
                {
                    MessageBox.Show("Couln't get column", "Error!");
                    return;
                }

                /* Read data from Table */
                Dictionary<string, int> mostCommonEmailDomains_Raw = new Dictionary<string, int>();
                while (reader.Read())
                {
                    string email = reader.GetString(0);
                    if (!email.Contains("@"))
                    {
                        MessageBox.Show("Field: " + email + " invalid email format!");
                        return;
                    }
                    string emailDomain = email.Substring((email.IndexOf("@") + 1), email.Length - (email.IndexOf("@") + 1));
                    /* Sort emails into dictionary */
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

                if (_NumberOfEmailRecords > mostCommonEmailDomains_Raw.Count)
                {
                    MessageBox.Show("Number of records is too high!", "Error!");
                    return;
                }

                /* Add fields to the list in UI */
                int iteration = _NumberOfEmailRecords;
                foreach (KeyValuePair<string, int> item in mostCommonEmailDomains_Raw.OrderByDescending(key => key.Value))
                {
                    iteration--;
                    if (iteration < 0)
                    {
                        break;
                    }
                    string field = "#" + item.Value.ToString() + " " + item.Key;
                    ListView_MostCommonEmails.Items.Add(field);
                }
                connection.Close();
                SQLiteConnection.ClearAllPools();

            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
            }

        }       
        #endregion Execute
        
        #region Utility
        private string[] ParseLine(string rowLine)
        {
            string newSeparator = "~";
            string tempRowLine = rowLine.Replace(TXT_Box_CSV_Separator.Text, newSeparator);
            return tempRowLine.Split(newSeparator.ToCharArray());

        }
        private static string RemoveSymbolsInBeginAndEnd(string p_EntryString, char character)
        {
            if (p_EntryString.Contains(character))
            {
                if (p_EntryString[0] == character)
                    p_EntryString = p_EntryString.Remove(0, 1);
                if (p_EntryString[p_EntryString.Length - 1] == character)
                    p_EntryString = p_EntryString.Remove(p_EntryString.Length - 1);
            }
            return p_EntryString;
        }
        void ShowProcessingDataTime()
        {
            if (_ProcessingDataTime.Elapsed.TotalMilliseconds < 1000)
            {
                double time = Math.Truncate(_ProcessingDataTime.Elapsed.TotalMilliseconds * 100) / 100;
                TXT_Block_LoadingTime.Text = "Loading time: " + time.ToString() + "ms";
            }
            else
            {
                double time = Math.Truncate(_ProcessingDataTime.Elapsed.TotalSeconds * 100) / 100;
                TXT_Block_LoadingTime.Text = "Loading time: " + time.ToString() + "s";
            }
        }
        private bool CheckEmailFieldsValidity()
        {
            if (TXT_Box_EmailColumn.Text == string.Empty)
            {
                MessageBox.Show("Insert Email column name!", "Error!");
                return false;
            }
            if (TXT_Box_NumberOfRecords.Text == string.Empty)
            {
                MessageBox.Show("Insert number of records!", "Error!");
                return false;
            }
            if (!int.TryParse(TXT_Box_NumberOfRecords.Text, out _NumberOfEmailRecords))
            {
                MessageBox.Show("You can only use numbers in records field!!", "Error!");
                return false;
            }
            return true;
        }
        private bool CheckSeparatorFieldValidity()
        {
            if (TXT_Box_CSV_Separator.Text == string.Empty)
            {
                MessageBox.Show("Insert Separator symbol", "Error!");
                return false;
            }
            if (TXT_Box_CSV_NewMainTableName.Text == string.Empty)
            {
                MessageBox.Show("Insert New Table Name!", "Error!");
                return false;
            }
            if (TXT_Box_CSV_NewDBName.Text == string.Empty)
            {
                MessageBox.Show("Insert New Database name!", "Error!");
                return false;
            }
            return true;
        }
        private bool CheckPostcodeFieldValidity()
        {
            if (TXT_Box_PostcodeColumnName.Text == string.Empty)
            {
                MessageBox.Show("Insert Postcode name symbol", "Error!");
                return false;
            }
            return true;
        }
        private bool CheckDatabaseTableNameFieldValidity()
        {
            if (TXT_Box_Database_TableName.Text == string.Empty)
            {
                MessageBox.Show("Insert Table Name", "Error!");
                return false;
            }
            return true;
        }
        private SQLiteDataReader GetColumnReader_SQLiteCommand(SQLiteCommand command, string p_database, string p_column)
        {
            try
            {
                command.CommandText = "SELECT " + p_column + " FROM " + p_database;
                command.ExecuteNonQuery();
                SQLiteDataReader reader = command.ExecuteReader();
                return reader;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return null;
            }
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
        void DropTableIfExist(SQLiteCommand p_Command, string p_CommandText)
        {
            try
            {
                p_Command.CommandText = p_CommandText;
                p_Command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
            }
        }
        bool CheckIfTableExist(SQLiteCommand p_Command, string p_CommandText)
        {
            try
            {
                p_Command.CommandText = p_CommandText;
                p_Command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return false;
            }
            return true;
        }
        bool DeleteTable(SQLiteCommand p_Command, string p_CommandText)
        {
            try
            {
                p_Command.CommandText = p_CommandText;
                p_Command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return false;
            }
            return true;
        }
        private void ResetToDefault()
        {
            TXT_Block_LoadingTime.Text = "";
            TXT_Box_EmailColumn.Text = "email";
            TXT_Box_NumberOfRecords.Text = "1";
            TXT_Box_CSV_Separator.Text = "\",\"";
            TXT_Box_Database_TableName.Text = "MyNewTable";
            TXT_Box_CSV_NewDBName.Text = "MyNewDatabase";
            BTN_Import.IsEnabled = false;
            BTN_DownloadData.IsEnabled = true;            

            _Data = null;
            _Data = new DataTable();
            _DataCoordinates = null;
            _DataCoordinates = new DataTable();

            CB_Csv.IsChecked = true;
            dataGrid.DataContext = null;
            DataGrid_Coordinates.DataContext = null;

            _ProcessingDataTime = null;
            _ProcessingDataTime = new Stopwatch();

            SetVisibility(Panel_MostCommonEmailAddresses, Visibility.Hidden);
            SetVisibility(Panel_LargestNumberOfPeopleLivingClose, Visibility.Hidden);
            SetActive(Panel_DirectorySelection, true);
        }
        #endregion Utility
    }
}
