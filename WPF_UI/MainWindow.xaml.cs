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
using Microsoft.Win32; // OpenFileDialog
using System.IO; // StreamReader
using System.Data; // DataTable
using System.Data.SQLite; //  SQLite
using System.Data.SqlClient;
using SqliteWrapper;
using Microsoft.CSharp;
using System.Net.Http;
using Newtonsoft.Json; // Serialization
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
        DataTable _DataPeopleGeoCloseResult;
        string _CurrentDatabase_Source;
        string _CurrentDatabase_MainTableName;
        string _CurrentDatabase_CoordinatesTableName;
        string _Field_CSV_Directory;
        string _Field_CSV_Separator;
        string _Field_CSV_NewDBName;
        string _Field_CSV_NewMainTableName;
        string _PostcodeColumnName;

        int _NumberOfEmailRecords;
        int _NumberOfPeopleGeoCloseRecords;
        int _MinAmountOfPeoplePerCloster;
        int _RangeBetweenEachLocation;
        Stopwatch _ProcessingDataTime;
        const int _HEARTHRADIUS = 6371; //Wikipedia

        public class ResponseObject
        {
            public int status;
            public Result[] result;
        }
        public class Result
        {
            public string query;
            public ResultData result;
        }
        public class ResultData
        {
            public string postcode;
            public float longitude;
            public float latitude;
        }
        enum PointLabel
        {
            NOT_PROCESSED,
            NOISE,
            CLUSTER
        }
        struct Vector2d
        {
            public double _X;
            public double _Y;
        }
        class Location
        {
            public string _Postcode;
            public int _ClusterID;
            public Vector2d _Coordinates;
            public PointLabel _Label;
        }

        public MainWindow()
        {
            InitializeComponent();
            ResetToDefault();
                     
            _CurrentDatabase_CoordinatesTableName = "Coordinates";
        }

        #region UI interaction
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
        private async void BTN_Import_Click(object sender, RoutedEventArgs e)
        {
            ShowOverlayMessage("Importing...");
            if (CB_Csv.IsChecked == true)
            {
                if (CheckImportFieldsValidityAndSetupValues() == true)
                {
                    Task<bool> Task_ReadFromCSV = new Task<bool>(ReadDataFromFileCSV);                     
                    Task_ReadFromCSV.Start();
                    bool Result_Task_ReadFromCSV = await Task_ReadFromCSV;
                    if (!Result_Task_ReadFromCSV)
                    {
                        MessageBox.Show("Could not read from CSV!", "Error!");
                        HideOverlayMessage();
                        return;
                    }                    
                    //dataGrid.DataContext = _Data.DefaultView;
                    TXT_Block_NumberOfRecordsImported.Text = _Data.Rows.Count.ToString();
                   
                    Task<bool> Task_CreateNewDBandTable = new Task<bool>(CreateNewDatabaseAndTable);
                    Task_CreateNewDBandTable.Start();                    
                    bool Result_Task_CreateNewDBandTable = await Task_CreateNewDBandTable;
                    if (!Result_Task_CreateNewDBandTable)
                    {
                        MessageBox.Show("Could not create New DB and table!", "Error!");
                        HideOverlayMessage();
                        return;
                    }
                    ShowProcessingDataTime();
                }
            }
            else if (CB_Database.IsChecked == true)
            {
                if (CheckDatabaseTableNameFieldValidityAndSetupValues() == true)
                {
                    Task<bool> Task_ReadFromExistingDB = new Task<bool>(ReadDataFromExistingDB);
                    Task_ReadFromExistingDB.Start();
                    bool Result_Task_eadFromExistingDB = await Task_ReadFromExistingDB;
                    if (!Result_Task_eadFromExistingDB)
                    {
                        MessageBox.Show("Could not create read data from existing DB!", "Error!");
                        HideOverlayMessage();
                        return;
                    }
                }
            }
            dataGrid.DataContext = _Data.DefaultView;
            HideOverlayMessage();
            SetActive(Panel_MostCommonEmailAddresses, true);
            SetActive(Panel_PeopleGeoClose, true);
            SetActive(Panel_DirectorySelection, false);
        }
        private async void BTN_FetchDataFromAPI_Click(object sender, RoutedEventArgs e)
        {
            if (CheckPostcodeFieldValidity() == true)
            {
                ShowOverlayMessage("Working...");
                if (!CreateNewCoordinateTableInDatabase())
                {
                    HideOverlayMessage();
                    return;
                }
                var postcodes = GetPostcodesFromDBColumn();                
                if (postcodes == null)
                {
                    HideOverlayMessage();
                    return;
                }
                
                bool Result_FetchingData = await FetchDataFromAPIWithPostcodes(postcodes);
                if (!Result_FetchingData)
                {
                    MessageBox.Show("Fetching data failed!", "Error!");
                    HideOverlayMessage();
                    return;
                }
                HideOverlayMessage();
                SetActive(Panel_CalculatePeopleGeoClose, true);
            }
                   
        }
        private void BTN_Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetToDefault();
        }
        private async void BTN_CalculatePeopleGeoClose_Click(object sender, RoutedEventArgs e)
        {
            if (CheckPeopleGeoCloseFieldsValidity() == true)
            {
                DataGrid_Result_PeopleGeoClose.DataContext = null;
                Task<bool> Task_DBScanAlgorithm = new Task<bool>(ExecuteDBScanAlgorithm);
                Task_DBScanAlgorithm.Start();
                ShowOverlayMessage("Calculating...");

                bool DBScanAlgorithmResult = await Task_DBScanAlgorithm;
                if (DBScanAlgorithmResult == false)
                {
                    MessageBox.Show("DBScan Algorithm failed!", "Error");
                    HideOverlayMessage();
                    return;
                }
                DataGrid_Result_PeopleGeoClose.DataContext = _DataPeopleGeoCloseResult.DefaultView;
                HideOverlayMessage();
            }
        }
        #endregion UI interaction
  

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
        private void InsertDataInMainTableFromStream(StreamReader p_StreamReader)
        {
            try
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

                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
            }
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
        private bool CreateNewDatabaseAndTable()
        {           
            try
            {
                _ProcessingDataTime.Start();
                SQLiteConnection.CreateFile(_Field_CSV_NewDBName);
                _CurrentDatabase_Source = @"Data Source = " + _Field_CSV_NewDBName + "; version=3; ";
                _CurrentDatabase_MainTableName = _Field_CSV_NewMainTableName;

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
                    return false;
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
                        return false;
                    }
                }
                transaction.Commit();
                _ProcessingDataTime.Stop();
                conn.Close();
                SQLiteConnection.ClearAllPools();
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
                return false;
            }

        }
        private bool CreateNewCoordinateTableInDatabase()
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
                    return false;
                }
                transaction.Commit();
                connection.Close();
                SQLiteConnection.ClearAllPools();
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
                return false;
            }
        }
        public List<string> GetPostcodesFromDBColumn()
        {           
            try
            {            
                List<string> postcodes = new List<string>();

                /* Setup Database connection */
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);
                var transaction = connection.BeginTransaction();

                SQLiteDataReader reader = GetColumnReader_SQLiteCommand(command, _CurrentDatabase_MainTableName, _PostcodeColumnName);
                if (reader == null)
                {
                    MessageBox.Show("Couln't get column", "Error!");
                    return null;
                }                
                /* Add each postcode to a list */
                while (reader.Read())
                {
                    string value = reader.GetString(0);
                    postcodes.Add(value);
                }
                transaction.Commit();
                return postcodes;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return null;
            }
        }        
        #endregion Create

        #region Read
        private bool ReadDataFromExistingDB()
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

                connection.Close();
                SQLiteConnection.ClearAllPools();
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
                return false;
            }
        }
        private bool ReadDataFromFileCSV()
        {
            try
            {
                StreamReader sr = new StreamReader(_Field_CSV_Directory);

                _ProcessingDataTime = Stopwatch.StartNew();
                /* Setup Header */
                string header = sr.ReadLine();
                if (string.IsNullOrEmpty(header))
                {
                    MessageBox.Show("No data in file");
                    return false;
                }
                string[] headerColumns = ParseLine(header);
                             
                foreach (string headerColumn in headerColumns)
                {
                    string cleanHeaderColumn = "";
                    cleanHeaderColumn = RemoveSymbolsInBeginAndEnd(headerColumn, '"');
                    _Data.Columns.Add(cleanHeaderColumn);
                }

                InsertDataInMainTableFromStream(sr);

                _ProcessingDataTime.Stop();

                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
                return false;
            }
        }       
        private async Task<bool> FetchDataFromAPIWithPostcodes(List<string> p_Postcodes)
        {
            try
            {
                _DataCoordinates = null;
                _DataCoordinates = new DataTable();
                /* Setup Headers */
                _DataCoordinates.Columns.Add("postcode");
                _DataCoordinates.Columns.Add("longitude");
                _DataCoordinates.Columns.Add("latitude");

                HttpClient client = new HttpClient();
                /* Process request for coordinates to API */
                int PostCodeIndex = 0;
                while (PostCodeIndex < p_Postcodes.Count)
                {
                    string json = "{\"postcodes\":[";
                    // 100 is the max amount of postcodes that the API can handle
                    for (int i = 0; i < 100; i++)
                    {                        
                        json += "\"" + p_Postcodes[PostCodeIndex] + "\",";
                        PostCodeIndex++;
                        // Break if the total number of postcodes is < 100
                        if (PostCodeIndex == p_Postcodes.Count)
                        {
                            break;
                        }
                    }
                    /* Remove last "," */
                    json = json.Remove(json.Length - 1);
                    json += "]}";
                    ShowOverlayMessage("Downloaded coordinates: " + PostCodeIndex.ToString() + "/" + p_Postcodes.Count.ToString());
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
                        return false;
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
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString(), "Error!");
                HideOverlayMessage();
                return false;
            }
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
                    MessageBox.Show("Couln't get email column", "Error!");
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
        private bool ExecuteDBScanAlgorithm()
        {
            try
            {
                /* Setup coordinate list */
                List<Location> Locations = new List<Location>();
                for (int i = 0; i < _DataCoordinates.Rows.Count; i++)
                {
                    float longitude;
                    float latitude;
                    bool LongitudeResult = float.TryParse(_DataCoordinates.Rows[i]["longitude"].ToString(), out longitude);
                    bool LatitudeResult = float.TryParse(_DataCoordinates.Rows[i]["latitude"].ToString(), out latitude);
                    if (LongitudeResult && LatitudeResult)
                    {
                        Location xitem = new Location();
                        xitem._ClusterID = 0;
                        xitem._Postcode = _DataCoordinates.Rows[i]["postcode"].ToString();
                        xitem._Coordinates = ConvertGeoCoordinatesToCartesian(longitude, latitude);
                        xitem._Label = PointLabel.NOT_PROCESSED;
                        Locations.Add(xitem);
                    }
                }
                int closterIndex = 0;

                List<List<Location>> results = new List<List<Location>>();
                Stopwatch timer = Stopwatch.StartNew();

                /* Algorithm */
                foreach (Location location in Locations)
                {
                    if (location._Label != PointLabel.NOT_PROCESSED)
                    {
                        continue;
                    }

                    var neighbors = GetNeighbors(Locations, location, _RangeBetweenEachLocation);

                    if (neighbors.Count < _MinAmountOfPeoplePerCloster)
                    {
                        location._Label = PointLabel.NOISE;
                        continue;
                    }
                    neighbors.Remove(location);
                    var seeds = neighbors;

                    closterIndex++;
                    location._ClusterID = closterIndex;

                    List<Location> clusterItems = new List<Location>(); ;

                    for (int i = 0; i < seeds.Count; i++)
                    {
                        if (seeds[i]._Label == PointLabel.NOISE || seeds[i]._Label != PointLabel.NOT_PROCESSED)
                        {
                            continue;
                        }
                        seeds[i]._Label = PointLabel.CLUSTER;
                        seeds[i]._ClusterID = closterIndex;
                        clusterItems.Add(seeds[i]);

                        var seedNeighbors = GetNeighbors(Locations, seeds[i], _RangeBetweenEachLocation);

                        if (seedNeighbors.Count >= _MinAmountOfPeoplePerCloster)
                        {
                            foreach (var seedNeighbor in seedNeighbors)
                            {
                                seeds.Add(seedNeighbor);
                            }
                        }
                    }
                    /* Add closter quantity to list */
                    if (clusterItems.Count >= _MinAmountOfPeoplePerCloster)
                    {
                        results.Add(clusterItems);
                    }
                }
                if (ElaborateGroupingResultsAndDisplay(results))
                {
                    return true;
                }
                return false;
                //timer.Stop();

            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString(), "Error!");
                return false;
            }
        }
        #endregion Execute

        #region Utility
        private void SetActive(UIElement p_Element, bool p_Active)
        {
            p_Element.IsEnabled = p_Active;
        }
        private void SetVisibility(UIElement p_Element, Visibility p_NewVisibility)
        {
            p_Element.Visibility = p_NewVisibility;
        }
        private string[] ParseLine(string rowLine)
        {
            string newSeparator = "~";
            string tempRowLine = rowLine.Replace(_Field_CSV_Separator, newSeparator);
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
        private void ShowProcessingDataTime()
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
        private bool CheckImportFieldsValidityAndSetupValues()
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
            _Field_CSV_Directory = TXT_Box_CSV_Directory.Text;
            _Field_CSV_Separator = TXT_Box_CSV_Separator.Text;
            _Field_CSV_NewDBName = TXT_Box_CSV_NewDBName.Text;
            _Field_CSV_NewMainTableName = TXT_Box_CSV_NewMainTableName.Text;
            return true;
        }
        private bool CheckPostcodeFieldValidity()
        {
            if (TXT_Box_PostcodeColumnName.Text == string.Empty)
            {
                MessageBox.Show("Insert Postcode name symbol", "Error!");
                return false;
            }
            _PostcodeColumnName = TXT_Box_PostcodeColumnName.Text;
            return true;
        }        
        private bool CheckPeopleGeoCloseFieldsValidity()
        {
            if (!int.TryParse(TXT_Box_LargestNumberOfPeopleCloseNumberRecords.Text, out _NumberOfPeopleGeoCloseRecords))
            {
                MessageBox.Show("You can only use numbers in records field!!", "Error!");
                return false;
            }

            bool parsingResult_minAmount = int.TryParse(TXT_Box_MinAmountPointsPerCluster.Text, out _MinAmountOfPeoplePerCloster);
            if (!parsingResult_minAmount)
            {
                MessageBox.Show("Insert valid number in field minimum amount of points per cluster!", "Error!");
                return false;
            }
            if (parsingResult_minAmount)
            {
                if(_MinAmountOfPeoplePerCloster <= 1)
                {
                    MessageBox.Show("Insert number > 2 in field minimum amount of points per cluster!", "Error!");
                    return false;
                }               
            }

            bool parsingResult_Range = int.TryParse(TXT_Box_ClusteringRange.Text, out _RangeBetweenEachLocation);
            if (!parsingResult_Range)
            {
                MessageBox.Show("Insert valid number in range!", "Error!");
                return false;
                
            }
            if (parsingResult_Range)
            {
                if (_RangeBetweenEachLocation <= 0)
                {
                    MessageBox.Show("Insert number > 0 in field range!", "Error!");
                    return false;
                }
            }
            return true;
        }
        private bool CheckDatabaseTableNameFieldValidityAndSetupValues()
        {
            if (TXT_Box_Database_TableName.Text == string.Empty)
            {
                MessageBox.Show("Insert Table Name", "Error!");
                return false;
            }
            _CurrentDatabase_MainTableName = TXT_Box_Database_TableName.Text;
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
        private void DropTableIfExist(SQLiteCommand p_Command, string p_CommandText)
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
        private bool CheckIfTableExist(SQLiteCommand p_Command, string p_CommandText)
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
        private bool DeleteTable(SQLiteCommand p_Command, string p_CommandText)
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
            /* Reset fields to default */
            TXT_Block_LoadingTime.Text = "Loading time: ";
            TXT_Box_EmailColumn.Text = "email";
            TXT_Box_NumberOfRecords.Text = "1";
            TXT_Box_CSV_Separator.Text = "\",\"";
            TXT_Box_Database_TableName.Text = "MyNewTable";
            TXT_Box_CSV_NewDBName.Text = "MyNewDatabase.db";           
            TXT_Box_LargestNumberOfPeopleCloseNumberRecords.Text = "3";
            TXT_Box_ClusteringRange.Text = "150";
            TXT_Box_MinAmountPointsPerCluster.Text = "2";

            /* Reset Data tables */
            _Data = null;
            _Data = new DataTable();
            _DataCoordinates = null;
            _DataCoordinates = new DataTable();
            _DataPeopleGeoCloseResult = null;
            _DataPeopleGeoCloseResult = new DataTable();

            /* Reset buttons */
            CB_Csv.IsChecked = true;
            BTN_Import.IsEnabled = false;
            BTN_DownloadData.IsEnabled = true;

            /* Reset grid tables */
            dataGrid.DataContext = null;
            DataGrid_Coordinates.DataContext = null;
            DataGrid_Result_PeopleGeoClose.DataContext = null;

            /* Reset timer */
            _ProcessingDataTime = null;
            _ProcessingDataTime = new Stopwatch();

            /* Reset panels */
            SetActive(Panel_DirectorySelection, true);
            SetActive(Panel_MostCommonEmailAddresses, false);
            SetActive(Panel_PeopleGeoClose, false);           
            SetActive(Panel_CalculatePeopleGeoClose, false);
        }
        private void ShowOverlayMessage(string p_Message)
        {
            SetVisibility(Panel_OverlayStatus, Visibility.Visible);
            TXT_Block_Status.Text = p_Message;
        }
        private void HideOverlayMessage()
        {
            SetVisibility(Panel_OverlayStatus, Visibility.Hidden);
        }
        private Vector2d ConvertGeoCoordinatesToCartesian(float p_Longitude, float p_Latitude)
        {
            Vector2d result;
            result._X = _HEARTHRADIUS * Math.Cos(p_Latitude) * Math.Cos(p_Longitude);
            result._Y = _HEARTHRADIUS * Math.Cos(p_Latitude) * Math.Sin(p_Longitude);
            return result;
        }
        private bool ElaborateGroupingResultsAndDisplay(List<List<Location>> results)
        {
            try
            {
                _DataPeopleGeoCloseResult = null;
                _DataPeopleGeoCloseResult = new DataTable();
                /* Setup Headers */
                _DataPeopleGeoCloseResult.Columns.Add("Cluster ID");
                _DataPeopleGeoCloseResult.Columns.Add("x");
                _DataPeopleGeoCloseResult.Columns.Add("y");
                for (int i = 0; i < _Data.Columns.Count; i++)
                {
                    _DataPeopleGeoCloseResult.Columns.Add(_Data.Columns[i].ColumnName);
                }

                int iteration = _NumberOfPeopleGeoCloseRecords;
                var ReorderedResultList = results.OrderByDescending(x => x.Count).ToList();
                /* Iterate through each cluster group */
                foreach (var Results in ReorderedResultList)
                {
                    iteration--;
                    if (iteration < 0)
                    {
                        break;
                    }
                    /* Iterate through each location in cluster */
                    foreach (var Location in Results)
                    {
                        DataRow NewRow = _DataPeopleGeoCloseResult.NewRow();
                        DataRow RowFromMainDB = null;
                        foreach (DataRow row in _Data.Rows)
                        {
                            var postcode = row.Field<string>("postal");
                            if (postcode == Location._Postcode)
                            {
                                RowFromMainDB = row;
                                break;
                            }
                        }

                        if (RowFromMainDB != null)
                        {
                            NewRow[0] = Location._ClusterID;
                            NewRow[1] = Location._Coordinates._X;
                            NewRow[2] = Location._Coordinates._Y;
                            for (int i = 0; i < RowFromMainDB.ItemArray.Length; i++)
                            {
                                NewRow[i + 3] = RowFromMainDB.ItemArray[i];
                            }
                            _DataPeopleGeoCloseResult.Rows.Add(NewRow);
                        }
                    }

                    DataRow EmptyRow = _DataPeopleGeoCloseResult.NewRow();
                    _DataPeopleGeoCloseResult.Rows.Add(EmptyRow);

                }
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString(), "Error!");
                return false;
            }
        }
        private double GetDistance(Vector2d p_location1, Vector2d p_location2)
        {
            return Math.Sqrt(Math.Pow((p_location2._X - p_location1._X), 2) + Math.Pow((p_location2._Y - p_location1._Y), 2));
        }
        private List<Location> GetNeighbors(List<Location> DB, Location checkingPoint, int range)
        {
            List<Location> neighbors = new List<Location>();
            foreach (Location point in DB)
            {
                double Distance = GetDistance(point._Coordinates, checkingPoint._Coordinates);
                if (Distance <= range)
                {
                    neighbors.Add(point);
                }
            }
            return neighbors;
        }
        #endregion Utility

    }

}
