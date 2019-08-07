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
        /// <summary>
        /// Main response object from API
        /// </summary>                               
        public class ResponseObject
        {
            public int status;
            public Result[] result;
        }
        /// <summary>
        /// Result object, part of main response from API
        /// </summary>   
        public class Result
        {
            public string query;
            public ResultData result;
        }
        /// <summary>
        /// Result data, part of result object from API
        /// </summary>  
        public class ResultData
        {
            public string postcode;
            public float longitude;
            public float latitude;
        }
        /// <summary>
        /// Property used in in DBScan algorithm for labeling each point
        /// </summary> 
        enum PointLabel
        {
            NOT_PROCESSED,
            NOISE,
            CLUSTER
        }
        /// <summary>
        /// Cartesian coordinates
        /// </summary> 
        struct Vector2d
        {
            public double _X;
            public double _Y;
        }
        /// <summary>
        /// Used in DBScan algorithm to store properties during the calculation
        /// </summary> 
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
        /// <summary>
        /// Called when checking the box for the database selection in the UI.
        /// <br/>
        /// Handle logic for enable/disabling selection buttons.
        /// </summary> 
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

        /// <summary> 
        /// Called when checking the box for the CSV selection in the UI.
        /// <br/>
        /// Handle logic for enable/disabling selection buttons.
        /// </summary> 
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

        /// <summary>
        /// Called when CSV "Browse" button is pressed in the UI.
        /// <br/>
        /// Open browser interface for selecting CSV files.
        /// </summary> 
        private void BTN_CSV_Browse_Click(object sender, RoutedEventArgs e)
        {
            TXT_Box_CSV_Directory.Text = GetFileNameFromFileSelection(".csv");

        }

        /// <summary>
        /// Called when Database "Browse" button is pressed in the UI.
        /// <br/>
        /// Open browser interface for selecting .db files.
        /// </summary> 
        private void BTN_Database_Browse_Click(object sender, RoutedEventArgs e)
        {
            TXT_Box_Database_Directory.Text = GetFileNameFromFileSelection(".db");
            _CurrentDatabase_Source = @"Data Source = " + TXT_Box_Database_Directory.Text + "; version=3; ";
        }

        /// <summary> 
        /// Called when "Execute" button is pressed in the "Most common email addresses" panel.
        /// <br/>
        /// Execute "Most common email addresses process".
        /// </summary> 
        private void BTN_ProcessMostCommonEmailAddresses_Click(object sender, RoutedEventArgs e)
        {
            if (CheckEmailFieldsValidity() == true)
            {
                ListView_MostCommonEmails.Items.Clear();
                ExecuteMostCommonEmailInUI();
            }            
        }

        /// <summary>
        /// Called when "Import" button is pressed in the "Selection" panel.
        /// <br/>
        /// Execute importing and displaying data in grid process.
        /// </summary> 
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
                    TXT_Block_NumberOfRecordsImported.Text = "Number of Records: " + _Data.Rows.Count.ToString();
                   
                    Task<bool> Task_CreateNewDBandTable = new Task<bool>(CreateNewSQLiteDatabaseAndTableFromMainDataTable);
                    Task_CreateNewDBandTable.Start();                    
                    bool Result_Task_CreateNewDBandTable = await Task_CreateNewDBandTable;
                    if (!Result_Task_CreateNewDBandTable)
                    {
                        MessageBox.Show("Could not create New DB and table!", "Error!");
                        HideOverlayMessage();
                        return;
                    }
                    TXT_Block_LoadingTime.Text = GetElepsedTime();
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

        /// <summary>
        /// Called when "Fetch" button is pressed in the "Fetch data from Postcodes.io" panel.
        /// <br/>
        /// Execute process for fetching data from Postcodes.io API.
        /// </summary> 
        private async void BTN_FetchDataFromAPI_Click(object sender, RoutedEventArgs e)
        {
            if (CheckPostcodeFieldValidity() == true)
            {
                ShowOverlayMessage("Working...");
                if (!CreateNewCoordinateSQLiteTableInSQLiteDatabase())
                {
                    HideOverlayMessage();
                    return;
                }
                var postcodes = GetPostcodesFromCurrentDB(_PostcodeColumnName);                
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

        /// <summary>
        /// Called when "Reset" button is pressed.
        /// <br/>
        /// Call "ResetToDefault" function to reset values to default.
        /// </summary> 
        private void BTN_Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetToDefault();
        }

        /// <summary>
        /// Called when "Execute" button is pressed in "Calculate largest groups of people geografically close" panel.
        /// <br/>
        /// Execute process to calculate the largest groups of people geografically close to each other. 
        /// </summary> 
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
        /// <summary> Read rows from reader and create new rows in data table. </summary> 
        /// <param name="p_Reader"> Reader from where to read data. </param>
        /// <param name="p_DataTable"> DataTable reference to where insert the rows. </param>
        private void InsertRowsInDataTable(SQLiteDataReader p_Reader, ref DataTable p_DataTable)
        {
            while (p_Reader.Read())
            {
                DataRow newRow = p_DataTable.NewRow();
                for (int i = 1; i < p_Reader.FieldCount; i++)
                {
                    string value = p_Reader.GetString(i);
                    newRow[i - 1] = value;
                }
                p_DataTable.Rows.Add(newRow);
            }
        }

        /// <summary> Insert row in SQLite Table. </summary>   
        /// <param name="p_Command"> SQLite Command object. </param>
        /// <param name="p_TableName"> Table name to where inserting the row. </param>
        /// <param name="p_HeaderSeries"> Serialized headers. </param>
        /// <param name="p_RowSeries"> Serialized row. </param>
        /// <returns> Return true if the rows were inserted in the table successfully. </returns>
        private bool InsertRowInSQLiteTable(SQLiteCommand p_Command, string p_TableName, string p_HeaderSeries, string p_RowSeries)
        {
            try
            {
                p_Command.CommandText = "INSERT INTO [" + p_TableName + "](" + p_HeaderSeries + ") VALUES (" + p_RowSeries + ")";
                p_Command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return false;
            }
            return true;
        }

        /// <summary> Read data from stream reader and insert data in data table </summary>   
        /// <param name="p_StreamReader"> Stream reader object. </param>
        /// <param name="p_DataTable"> Datatable reference to where add the data. </param>
        private void InsertDataInMainTableFromStream(StreamReader p_StreamReader, ref DataTable p_DataTable)
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
                    string[] values = ParseLine(line, "~");

                    for (int i = 0; i < values.Count(); i++)
                    {
                        importedRow[i] = RemoveSymbolsInBeginAndEnd(values[i], '"');
                    }
                    p_DataTable.Rows.Add(importedRow);                    
                }                 
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
            }
        }
        #endregion Insert

        #region Create
        /// <summary> Create SQLite table </summary>   
        /// <param name="p_Command"> SQLite command object. </param>
        /// <param name="p_TableName"> Table name of the new SQLite table. </param>
        /// <param name="p_Headers"> Serialized headers of the new table. </param>
        /// <returns> Return true if the table was created successfully. </returns>
        private bool CreateSQliteTable(SQLiteCommand p_Command, string p_TableName, string p_Headers)
        {
            try
            {
                p_Command.CommandText = "CREATE TABLE [" + p_TableName + "](" + "[Id] INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL" + p_Headers;
                p_Command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return false;
            }
            return true;
        }

        /// <summary> Create SQLite database and table (used for CSV importing process) </summary>   
        /// <returns> Return true if the database and table were created successfully. </returns>
        private bool CreateNewSQLiteDatabaseAndTableFromMainDataTable()
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

                if (!CreateSQliteTable(cmd, _CurrentDatabase_MainTableName, headers))
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

                    if (!InsertRowInSQLiteTable(cmd, _CurrentDatabase_MainTableName, headerSeries, rowSeries))
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

        /// <summary> Create New SQLite table in current DB (used to store coordinates from Postcodes.io API </summary>   
        /// <returns> Return true if the table was created successfully. </returns>
        private bool CreateNewCoordinateSQLiteTableInSQLiteDatabase()
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
                if (!CreateSQliteTable(command, _CurrentDatabase_CoordinatesTableName, headers))
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

        /// <summary> Get postcodes list from database </summary>  
        /// <param name="p_ColumnName"> Name of the column to get the postcodes.</param>
        /// <returns> Return the list of postcodes from the column. </returns>
        public List<string> GetPostcodesFromCurrentDB(string p_ColumnName)
        {           
            try
            {            
                List<string> postcodes = new List<string>();

                /* Setup Database connection */
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);
                var transaction = connection.BeginTransaction();

                SQLiteDataReader reader = GetReaderFromColumn(command, _CurrentDatabase_MainTableName, p_ColumnName);
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
        /// <summary> Read data from existing DB and store data into main data table. </summary>  
        /// <returns> Return true if the data was read and stored successfully. </returns>
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

        /// <summary> Read data from CSV file and store it in the main data table </summary>  
        /// <returns> Return true if the data was read and stored successfully. </returns>
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
                string[] headerColumns = ParseLine(header, "~");
                             
                foreach (string headerColumn in headerColumns)
                {
                    string cleanHeaderColumn = "";
                    cleanHeaderColumn = RemoveSymbolsInBeginAndEnd(headerColumn, '"');
                    _Data.Columns.Add(cleanHeaderColumn);
                }

                InsertDataInMainTableFromStream(sr, ref _Data);

                _ProcessingDataTime.Stop();

                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message);
                return false;
            }
        }

        /// <summary> Fetch data from Postcodes.io API and store values into the coordinate data table. </summary>  
        /// <param name="p_Postcodes"> List of poscodes to fetch .</param>
        /// <returns> Return true if the data was read and stored successfully. </returns>
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
        /// <summary> Execute process for calculating the most common email addresses. </summary>  
        private void ExecuteMostCommonEmailInUI()
        {
            try
            {
                SQLiteConnection connection = new SQLiteConnection(_CurrentDatabase_Source);
                connection.Open();
                SQLiteCommand command = new SQLiteCommand(connection);

                SQLiteDataReader reader = GetReaderFromColumn(command, _CurrentDatabase_MainTableName, TXT_Box_EmailColumn.Text);
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

        /// <summary> Execute process for calculating the largest groups of people geografically close to each other using the DBScan algorithm. </summary>  
        /// <returns> Return true if the algorithm went successfully. </returns>
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

        /// <summary> Elaborate result from DB algorithm and display result in data grid </summary>  
        /// <param name="p_Results"> List of clusters </param>
        /// <returns> Return true if elaboration went successfully. </returns>
        private bool ElaborateGroupingResultsAndDisplay(List<List<Location>> p_Results)
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
                var ReorderedResultList = p_Results.OrderByDescending(x => x.Count).ToList();
              
                /* Iterate through each cluster group */
                foreach (var Results in ReorderedResultList)
                {
                    iteration--;
                    if (iteration < 0)
                    {
                        break;
                    }
                    /* Add quantity row */
                    DataRow TitleRow = _DataPeopleGeoCloseResult.NewRow();
                    TitleRow[0] = "Quantity: " + Results.Count.ToString();
                    _DataPeopleGeoCloseResult.Rows.Add(TitleRow);

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
                            NewRow[1] = Math.Round(Location._Coordinates._X, 2);
                            NewRow[2] = Math.Round(Location._Coordinates._Y, 2);
                            for (int i = 0; i < RowFromMainDB.ItemArray.Length; i++)
                            {
                                NewRow[i + 3] = RowFromMainDB.ItemArray[i];
                            }
                            _DataPeopleGeoCloseResult.Rows.Add(NewRow);
                        }
                    }

              

                }
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString(), "Error!");
                return false;
            }
        }
        #endregion Execute

        #region Utility
        /// <summary> Activate or deactivate UI element. </summary>  
        /// <param name="p_Element"> UI Element to activate or deactivate. </param>
        /// <param name="p_Active"> New condition. </param>
        private void SetActive(UIElement p_Element, bool p_Active)
        {
            p_Element.IsEnabled = p_Active;
        }

        /// <summary> Trigger visibility of UI element. </summary>  
        /// <param name="p_Element"> UI Element to trigger visibility. </param>
        /// <param name="p_Active"> New visibility. </param>
        private void SetVisibility(UIElement p_Element, Visibility p_NewVisibility)
        {
            p_Element.Visibility = p_NewVisibility;
        }

        /// <summary> String parsing </summary>  
        /// <param name="p_Line"> String to parse. </param>
        /// <param name="p_Separator"> Separator. </param>
        /// <returns> Return an array with each element parsed. </returns>
        private string[] ParseLine(string p_Line, string p_Separator)
        {
            string tempRowLine = p_Line.Replace(_Field_CSV_Separator, p_Separator);
            return tempRowLine.Split(p_Separator.ToCharArray());
        }

        /// <summary> Remove specified character in beginning and end of entry string. </summary>  
        /// <param name="p_EntryString"> Entry string. </param>
        /// <param name="p_Character"> Character to remove. </param>
        /// <returns> Return the string without the character in the beginning or end.
        /// If it doesn't contain it, it returns the entry string </returns>
        private string RemoveSymbolsInBeginAndEnd(string p_EntryString, char p_Character)
        {
            if (p_EntryString.Contains(p_Character))
            {
                if (p_EntryString[0] == p_Character)
                    p_EntryString = p_EntryString.Remove(0, 1);
                if (p_EntryString[p_EntryString.Length - 1] == p_Character)
                    p_EntryString = p_EntryString.Remove(p_EntryString.Length - 1);
            }
            return p_EntryString;
        }

        /// <summary> Calculate elapsed time. </summary>  
        /// <returns> Return a string with the elapsed time. </returns>
        private string GetElepsedTime()
        {
            if (_ProcessingDataTime.Elapsed.TotalMilliseconds < 1000)
            {
                double time = Math.Truncate(_ProcessingDataTime.Elapsed.TotalMilliseconds * 100) / 100;
                return "Loading time: " + time.ToString() + "ms";
            }
            else
            {
                double time = Math.Truncate(_ProcessingDataTime.Elapsed.TotalSeconds * 100) / 100;
                return "Loading time: " + time.ToString() + "s";
            }
            
        }

        /// <summary> Check email panel fields validity and store field values in variables. </summary>  
        /// <returns> Return true if all the checks were successfull. </returns>
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

        /// <summary> Check import panel fields validity and store field values in variables. </summary>  
        /// <returns> Return true if all the checks were successfull. </returns>
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

        /// <summary> Check postcode panel fields validity and store field values in variables. </summary>  
        /// <returns> Return true if all the checks were successfull. </returns>
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

        /// <summary> Check People Geografically close to each other panel fields validity and store field values in variables. </summary>  
        /// <returns> Return true if all the checks were successfull. </returns>
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

        /// <summary> Check database field name and store field values in variables. </summary>  
        /// <returns> Return true if all the checks were successfull. </returns>
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

        /// <summary> Get SQLreader from read column </summary>  
        /// <param name="p_Command"> SQLite command. </param>
        /// <param name="p_Database"> Database name. </param>
        /// <param name="p_Column"> Column to read. </param>
        /// <returns> Return the reader if the command went successfully, otherwise return null. </returns>
        private SQLiteDataReader GetReaderFromColumn(SQLiteCommand p_Command, string p_Database, string p_Column)
        {
            try
            {
                p_Command.CommandText = "SELECT " + p_Column + " FROM " + p_Database;
                p_Command.ExecuteNonQuery();
                SQLiteDataReader reader = p_Command.ExecuteReader();
                return reader;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
                return null;
            }
        }

        /// <summary> Open File dialog for selecting file and store directory </summary>  
        /// <param name="p_FileExtension"> File extension. </param>
        /// <returns> Returns the directory string. </returns>
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

        /// <summary> Drop Table and remove it from database if exists </summary>  
        /// <param name="p_Command"> SQLite command. </param>
        /// <param name="p_CommandText"> Command text. </param>
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

        /// <summary> Check if table exists. </summary>  
        /// <param name="p_Command"> SQLite command. </param>
        /// <param name="p_CommandText"> Command text. </param>
        /// <returns> Return true if exists. </returns>
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

        /// <summary> Delete table </summary>  
        /// <param name="p_Command"> SQLite command. </param>
        /// <param name="p_CommandText"> Command text. </param>
        /// <returns> Return true if table was deleted. </returns>
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

        /// <summary> Reset values to default. </summary>  
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

        /// <summary> Show overlay panel, used for avoiding user inputs while async function is running </summary>  
        /// <param name="p_Message"> Message to display. </param>
        private void ShowOverlayMessage(string p_Message)
        {
            SetVisibility(Panel_OverlayStatus, Visibility.Visible);
            TXT_Block_Status.Text = p_Message;
        }

        /// <summary> Hide overlay panel, used for avoiding user inputs while async function is running </summary>  
        private void HideOverlayMessage()
        {
            SetVisibility(Panel_OverlayStatus, Visibility.Hidden);
        }

        /// <summary> Convert geographic coordinates into cartesian </summary>  
        /// <param name="p_Longitude"> Longitude </param>
        /// <param name="p_Latitude"> Latidute </param>
        /// <returns> Return a Vector2d class with cartesian coordinates. </returns>
        private Vector2d ConvertGeoCoordinatesToCartesian(float p_Longitude, float p_Latitude)
        {
            Vector2d result;
            float xLongitudeRadians = p_Longitude * (float)Math.PI / 180;
            float yLatitudeRadians = p_Latitude * (float)Math.PI / 180;
            result._X = _HEARTHRADIUS *  Math.Cos(yLatitudeRadians) * Math.Cos(xLongitudeRadians);
            result._Y = _HEARTHRADIUS * Math.Cos(yLatitudeRadians) * Math.Sin(xLongitudeRadians);
            return result;
        }

        /// <summary> Get distance between two vectors </summary>  
        /// <param name="p_location1"> Vector 1 </param>
        /// <param name="p_location2"> Vector 2 </param>
        /// <returns> Return the distance as double </returns>
        private double GetDistance(Vector2d p_location1, Vector2d p_location2)
        {
            return Math.Sqrt(Math.Pow((p_location2._X - p_location1._X), 2) + Math.Pow((p_location2._Y - p_location1._Y), 2));
        }

        /// <summary> Get list of neighbors in a range </summary>  
        /// <param name="p_Locations"> Database of locations. </param>
        /// <param name="p_CheckingPoint"> Location to where to check the neighbors. </param>
        /// <param name="p_Range"> Range. </param>
        /// <returns> Return a list of neighbors </returns>
        private List<Location> GetNeighbors(List<Location> p_Locations, Location p_CheckingPoint, int p_Range)
        {
            List<Location> neighbors = new List<Location>();
            foreach (Location point in p_Locations)
            {
                double Distance = GetDistance(point._Coordinates, p_CheckingPoint._Coordinates);
                if (Distance <= p_Range)
                {
                    neighbors.Add(point);
                }
            }
            return neighbors;
        }
        #endregion Utility

    }

}
