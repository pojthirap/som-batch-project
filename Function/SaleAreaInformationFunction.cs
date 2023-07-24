using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AzureFunctionApp.common;
using AzureFunctionApp.Model.Request.OutboundSaleAreaInformationRequest;
using AzureFunctionApp.Model.Response.OutboundSaleAreaInformationResponse;
using AzureFunctionApp.Utils;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using Microsoft.Extensions.Configuration;
using AzureFunctionApp.Common;

namespace AzureFunctionApp
{
    public  class SaleAreaInformationFunction
    {
        private readonly IConfiguration _configuration;
        private string connectionString_ = null;
        private string interfaceSapPassword_ = null;
        private string interfaceSapReqKey_ = null;
        public SaleAreaInformationFunction(IConfiguration configuration)
        {
            _configuration = configuration;
            string keyValue = _configuration[CommonConstant.KEYVALUE_TXT];
            string connectionString = _configuration[CommonConstant.CONNECTIONSTRING_TEXT];
            connectionString_ = String.IsNullOrEmpty(keyValue) ? connectionString : keyValue;

            string keyValueInterfaceSapPassword = _configuration[CommonConstant.KEYVALUE_SAP_INTERFACE_PASSWORD_TXT];
            string sapInterfacePassword = _configuration[CommonConstant.SAP_INTERFACE_PASSWORD_TEXT];
            interfaceSapPassword_ = String.IsNullOrEmpty(keyValueInterfaceSapPassword) ? sapInterfacePassword : keyValueInterfaceSapPassword;

            string keyValueInterfaceSapReqKey = _configuration[CommonConstant.KEYVALUE_SAP_INTERFACE_REQ_KEY_TXT];
            string sapInterfaceReqKey = _configuration[CommonConstant.SAP_INTERFACE_REQ_KEY_TEXT];
            interfaceSapReqKey_ = String.IsNullOrEmpty(keyValueInterfaceSapReqKey) ? sapInterfaceReqKey : keyValueInterfaceSapReqKey;

            Console.WriteLine("keyValue:" + keyValue);
            Console.WriteLine("connectionString:" + connectionString_);
            Console.WriteLine("keyValueInterfaceSapPassword:" + keyValueInterfaceSapPassword);
            Console.WriteLine("interfaceSapPassword_:" + interfaceSapPassword_);
            Console.WriteLine("keyValueInterfaceSapReqKey:" + keyValueInterfaceSapReqKey);
            Console.WriteLine("interfaceSapReqKey_:" + interfaceSapReqKey_);

        }

        [Function("SaleAreaInformationFunction")]
        public  async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger(CommonConstant.GetLoggerString);
            logger.LogInformation("C# HTTP trigger function processed a request. SaleAreaInformationFunction");

            var responses = req.CreateResponse(HttpStatusCode.OK);
            responses.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            responses.WriteString("connectionString : " + connectionString_);

            // Get Config From Database
            InterfaceSapConfig ic = await CommonConfigDB.getInterfaceSapConfigAsync(connectionString_, logger);
            ic.InterfaceSapPwd = interfaceSapPassword_;
            ic.ReqKey = interfaceSapReqKey_;

            // SQL Scope
            string VAL_SYNC_DATA_LOG_SEQ = "";
            using (SqlConnection connection = new SqlConnection(connectionString_))
            {
                connection.Open();
                // Create SQL
                String sql = "select NEXT VALUE FOR SYNC_DATA_LOG_SEQ as SEQ_";
                SqlCommand command = connection.CreateCommand();
                // Execute SQL
                logger.LogDebug("Query:" + sql);
                Console.WriteLine("Query:" + sql);
                command.CommandText = sql;
                
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        VAL_SYNC_DATA_LOG_SEQ = reader.GetValue(reader.GetOrdinal("SEQ_")).ToString();
                    }
                }


                command = connection.CreateCommand();
                SqlTransaction transaction;

                // Start a local transaction.
                transaction = connection.BeginTransaction("T1");

                // Must assign both transaction object and connection
                // to Command object for a pending local transaction
                command.Connection = connection;
                command.Transaction = transaction;

                try
                {
                    // INSERT SYNC_DATA_LOG
                    // Create SQL
                    command.Parameters.Clear();
                    StringBuilder bulder = new StringBuilder();
                    bulder.AppendFormat(" INSERT INTO SYNC_DATA_LOG ([SYNC_ID], [INTERFACE_ID], [CREATE_USER], [CREATE_DTM]) ");
                    bulder.AppendFormat(" VALUES(@VAL_SYNC_DATA_LOG_SEQ, 'ZSOMI006', 'SYSTEM', dbo.GET_SYSDATETIME()); ");
                    command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                    command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;

                    // Execute SQL
                    logger.LogDebug("Query:" + bulder.ToString());
                    Console.WriteLine("Query:" + bulder.ToString());
                    command.CommandText = bulder.ToString();
                    Int32 rowsAffected = command.ExecuteNonQuery();
                    logger.LogDebug("RowsAffected:" + rowsAffected);
                    Console.WriteLine("RowsAffected:" + rowsAffected);

                    // Attempt to commit the transaction.
                    transaction.Commit();
                    logger.LogDebug("Commit to database.");
                    Console.WriteLine("Commit to database.");
                }
                catch (Exception ex)
                {
                    logger.LogDebug("Commit Exception Type: {0}", ex.GetType());
                    logger.LogDebug("  Message: {0} {1}", ex.Message, ex.ToString());
                    Console.WriteLine("Commit Exception Type: {0}", ex.GetType());
                    Console.WriteLine("  Message: {0} {1}", ex.Message, ex.ToString());
                    responses = req.CreateResponse(HttpStatusCode.InternalServerError);
                    // Attempt to roll back the transaction.
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception ex2)
                    {
                        // This catch block will handle any errors that may have occurred
                        // on the server that would cause the rollback to fail, such as
                        // a closed connection.
                        logger.LogDebug("Rollback Exception Type: {0}", ex2.GetType());
                        logger.LogDebug("  Message: {0} {1}", ex2.Message, ex2.ToString());

                        Console.WriteLine("Rollback Exception Type: {0}", ex2.GetType());
                        Console.WriteLine("  Message: {0} {1}", ex2.Message, ex2.ToString());
                        responses = req.CreateResponse(HttpStatusCode.InternalServerError);
                        throw;
                    }
                }
            }
            // SQL Scope


            using (var client = new HttpClient())
            {
                OutboundSaleAreaInformationRequest reqData = new OutboundSaleAreaInformationRequest();
                RequestInput input = new RequestInput();
                input.Interface_ID = "ZSOMI006";
                input.Table_Object = "V_TVTA_ASSIGN";
                input.All_data = "X";
                reqData.Input = input;

                client.BaseAddress = new Uri(ic.InterfaceSapUrl + CommonConstant.API_OutboundSaleAreaInformation);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ic.InterfaceSapUser}:{ic.InterfaceSapPwd}")));
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", CommonConstant.USER_AGEN);
                client.DefaultRequestHeaders.Add(CommonConstant.ReqKey, ic.ReqKey);
                client.Timeout = TimeSpan.FromSeconds(Convert.ToDouble(CommonConstant.API_TIMEOUT));
                Console.WriteLine(client.DefaultRequestHeaders.ToString());
                Console.WriteLine(client.DefaultRequestHeaders.GetValues("req-key"));
                logger.LogInformation(client.DefaultRequestHeaders.ToString());
                //logger.LogInformation(client.DefaultRequestHeaders.GetValues("req-key"));
                string fullPath = ic.InterfaceSapUrl + CommonConstant.API_OutboundSaleAreaInformation;
                var jsonVal = JsonConvert.SerializeObject(reqData);
                var content = new StringContent(jsonVal, Encoding.UTF8, "application/json");
                logger.LogDebug("Call Outbound Service:" + ic.InterfaceSapUrl + CommonConstant.API_OutboundSaleAreaInformation);
                logger.LogDebug("=========== jsonVal ================");
                logger.LogDebug("REQUEST:" + jsonVal);
                logger.LogDebug("REQUEST:" + JObject.Parse(jsonVal));
                Console.WriteLine("Call Outbound Service:" + ic.InterfaceSapUrl + CommonConstant.API_OutboundSaleAreaInformation);
                Console.WriteLine("=========== jsonVal ================");
                Console.WriteLine("REQUEST:" + jsonVal);
                Console.WriteLine("REQUEST:" + JObject.Parse(jsonVal));

                // Cal use time
                // Create new stopwatch.
                Stopwatch stopwatch = new Stopwatch();

                // Begin timing.
                stopwatch.Start();
                //
                HttpResponseMessage response = await client.PostAsync(fullPath, content);

                // Stop timing.
                stopwatch.Stop();

                // Write result.
                logger.LogDebug("Time elapsed: {0}", stopwatch.Elapsed);
                logger.LogDebug("Time Use: {0}", stopwatch.Elapsed);
                Console.WriteLine("Time elapsed: {0}", stopwatch.Elapsed);
                Console.WriteLine("Time Use: {0}", stopwatch.Elapsed);

                OutboundSaleAreaInformationResponse resultOutbound = null;
                // SQL Scope
                using (SqlConnection connection = new SqlConnection(connectionString_))
                {
                    connection.Open();

   
                    SqlCommand command = connection.CreateCommand();
                    SqlTransaction transaction;

                    // Start a local transaction.
                    transaction = connection.BeginTransaction("T2");

                    // Must assign both transaction object and connection
                    // to Command object for a pending local transaction
                    command.Connection = connection;
                    command.Transaction = transaction;

                    try
                    {
                        resultOutbound = new OutboundSaleAreaInformationResponse();
                        List<Data> lst = new List<Data>();

                        if (response.IsSuccessStatusCode) // Call Success
                        {
                            resultOutbound = await response.Content.ReadAsAsync<OutboundSaleAreaInformationResponse>();
                            lst = resultOutbound.Data;
                            if (!"S".Equals(resultOutbound.Header.Status))
                            {
                                // Create SQL
                                command.Parameters.Clear();
                                StringBuilder bulder = new StringBuilder();
                                bulder.AppendFormat(" UPDATE SYNC_DATA_LOG  ");
                                bulder.AppendFormat(" SET [ERROR_DESC]=@Message, [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                bulder.AppendFormat(" WHERE [SYNC_ID]= @VAL_SYNC_DATA_LOG_SEQ ");
                                command.Parameters.Add("@Message", SqlDbType.NVarChar);
                                command.Parameters["@Message"].Value = resultOutbound.Header.Message;
                                command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                QueryUtils.configParameter(command);
                                // Execute SQL
                                logger.LogDebug("Query:" + bulder.ToString());
                                Console.WriteLine("Query:" + bulder.ToString());
                                command.CommandText = bulder.ToString();
                                Int32 rowsAffected = command.ExecuteNonQuery();
                                logger.LogDebug("RowsAffected:" + rowsAffected);
                                Console.WriteLine("RowsAffected:" + rowsAffected);
                                //EXIT Program
                            }
                        }
                        else
                        { // Call Error

                            //var ErrMsg = JsonConvert.DeserializeObject<dynamic>(response.Content.ReadAsStringAsync().Result);
                            string ErrMsg = response.Content.ReadAsStringAsync().Result;

                            // Create SQL
                            command.Parameters.Clear();
                            StringBuilder bulder = new StringBuilder();
                            bulder.AppendFormat(" UPDATE SYNC_DATA_LOG  ");
                            bulder.AppendFormat(" SET [ERROR_DESC]=@ErrMsg, [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                            bulder.AppendFormat(" WHERE [SYNC_ID]= @VAL_SYNC_DATA_LOG_SEQ ");
                            command.Parameters.Add("@ErrMsg", SqlDbType.NVarChar);
                            command.Parameters["@ErrMsg"].Value = ErrMsg;
                            command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                            command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                            QueryUtils.configParameter(command);
                            // Execute SQL
                            logger.LogDebug("Query:" + bulder.ToString());
                            Console.WriteLine("Query:" + bulder.ToString());
                            command.CommandText = bulder.ToString();
                            Int32 rowsAffected = command.ExecuteNonQuery();
                            logger.LogDebug("RowsAffected:" + rowsAffected);
                            Console.WriteLine("RowsAffected:" + rowsAffected);
                            //EXIT Program
                        }

                        logger.LogDebug("Response Outbound Service:" + ic.InterfaceSapUrl + CommonConstant.API_OutboundSaleAreaInformation);
                        logger.LogDebug("Response:" + JsonConvert.SerializeObject(resultOutbound));
                        Console.WriteLine("Response Outbound Service:" + ic.InterfaceSapUrl + CommonConstant.API_OutboundSaleAreaInformation);
                        Console.WriteLine("Response:" + JsonConvert.SerializeObject(resultOutbound));



                        // Attempt to commit the transaction.
                        transaction.Commit();
                        logger.LogDebug("Commit to database.");
                        Console.WriteLine("Commit to database.");
                    }
                    catch (Exception ex)
                    {
                        // Create SQL
                        command.Parameters.Clear();
                        StringBuilder bulder = new StringBuilder();
                        bulder.AppendFormat(" UPDATE SYNC_DATA_LOG  ");
                        bulder.AppendFormat(" SET [ERROR_DESC]=@ErrMsg, [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                        bulder.AppendFormat(" WHERE [SYNC_ID]= @VAL_SYNC_DATA_LOG_SEQ ");
                        command.Parameters.Add("@ErrMsg", SqlDbType.NVarChar);
                        command.Parameters["@ErrMsg"].Value = (ex.Message + ":" + ex.ToString());
                        command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                        command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                        // Execute SQL
                        logger.LogDebug("Query:" + bulder.ToString());
                        Console.WriteLine("Query:" + bulder.ToString());
                        command.CommandText = bulder.ToString();
                        Int32 rowsAffected = command.ExecuteNonQuery();
                        logger.LogDebug("RowsAffected:" + rowsAffected);
                        Console.WriteLine("RowsAffected:" + rowsAffected);

                        logger.LogDebug("Commit Exception Type: {0}", ex.GetType());
                        logger.LogDebug("  Message: {0} {1}", ex.Message, ex.ToString());
                        Console.WriteLine("Commit Exception Type: {0}", ex.GetType());
                        Console.WriteLine("  Message: {0} {1}", ex.Message, ex.ToString());
                        responses = req.CreateResponse(HttpStatusCode.InternalServerError);

                        // Attempt to commit the transaction.
                        transaction.Commit();
                        logger.LogDebug("Commit to database.");
                        Console.WriteLine("Commit to database.");
                        responses = req.CreateResponse(HttpStatusCode.InternalServerError);
                        throw;
                        // Attempt to roll back the transaction.
                        /*try
                        {
                            transaction.Rollback();
                        }
                        catch (Exception ex2)
                        {
                            // This catch block will handle any errors that may have occurred
                            // on the server that would cause the rollback to fail, such as
                            // a closed connection.
                            logger.LogDebug("Rollback Exception Type: {0}", ex2.GetType());
                            logger.LogDebug("  Message: {0}", ex2.Message);
                            
                        }*/
                    }
                }
                // SQL Scope


                if (resultOutbound != null)// Process Data 
                {
                    SqlTransaction transaction = null;
                    try
                    {
                        // SQL Scope
                        using (SqlConnection connection = new SqlConnection(connectionString_))
                        {
                            connection.Open();

                            SqlCommand command = connection.CreateCommand();

                            // Start a local transaction.
                            transaction = connection.BeginTransaction("T3");

                            // Must assign both transaction object and connection
                            // to Command object for a pending local transaction
                            command.Connection = connection;
                            command.Transaction = transaction;


                            // Begin Process In Database
                            if (resultOutbound.Data != null && resultOutbound.Data.Count != 0)
                            {

                                //List<String> VAL_GROUP_Distribution_Channel = resData.Data..stream().collect(Collectors.groupingBy(r->r.Distribution_Channel + "|" + r.Distribution_Channel_Name));
                                List<string> VAL_GROUP_Distribution_Channel = new List<string>();
                                var groupedBy = resultOutbound.Data.GroupBy(z => z.Distribution_Channel + "|" + z.Distribution_Channel_Name);
                                foreach (var group in groupedBy)
                                {
                                    VAL_GROUP_Distribution_Channel.Add(group.Key);
                                }

                                //
                                StringBuilder bulder = null;
                                Int32 rowsAffected = 0;

                                foreach (string s in VAL_GROUP_Distribution_Channel)
                                {
                                    string[] G = s.Split("|");
                                    // INSERT SYNC_DATA_LOG
                                    // Create SQL
                                    command.Parameters.Clear();
                                    bulder = new StringBuilder();

                                    bulder.AppendFormat(" UPDATE ORG_DIST_CHANNEL  ");
                                    bulder.AppendFormat(" SET [SYNC_ID]=@VAL_SYNC_DATA_LOG_SEQ, [CHANNEL_NAME_TH]=@G1, [CHANNEL_NAME_EN]=@G1, [ACTIVE_FLAG]='Y', [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                    bulder.AppendFormat(" WHERE [CHANNEL_CODE]=@G0 ");

                                    command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                    command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                    command.Parameters.Add("@G1", SqlDbType.NVarChar);
                                    command.Parameters["@G1"].Value = G[1];
                                    command.Parameters.Add("@G0", SqlDbType.NVarChar);
                                    command.Parameters["@G0"].Value = G[0];
                                    QueryUtils.configParameter(command);

                                    // Execute SQL
                                    logger.LogDebug("Query:" + bulder.ToString());
                                    Console.WriteLine("Query:" + bulder.ToString());
                                    command.CommandText = bulder.ToString();
                                    rowsAffected = command.ExecuteNonQuery();
                                    logger.LogDebug("RowsAffected:" + rowsAffected);
                                    Console.WriteLine("RowsAffected:" + rowsAffected);

                                    if (rowsAffected == 0)
                                    {
                                        // Create SQL
                                        command.Parameters.Clear();
                                        bulder = new StringBuilder();

                                        bulder.AppendFormat(" INSERT INTO ORG_DIST_CHANNEL ([CHANNEL_CODE], [CHANNEL_NAME_TH], [CHANNEL_NAME_EN], [ACTIVE_FLAG], [SYNC_ID], [CREATE_USER], [CREATE_DTM], [UPDATE_USER], [UPDATE_DTM])  ");
                                        bulder.AppendFormat(" VALUES(@G0, @G1, @G1, 'Y', @VAL_SYNC_DATA_LOG_SEQ, 'SYSTEM', dbo.GET_SYSDATETIME(), 'SYSTEM', dbo.GET_SYSDATETIME()) ");
                                        command.Parameters.Add("@G0", SqlDbType.NVarChar);
                                        command.Parameters["@G0"].Value = G[0];
                                        command.Parameters.Add("@G1", SqlDbType.NVarChar);
                                        command.Parameters["@G1"].Value = G[1];
                                        command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                        command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                        QueryUtils.configParameter(command);
                                        // Execute SQL
                                        logger.LogDebug("Query:" + bulder.ToString());
                                        Console.WriteLine("Query:" + bulder.ToString());
                                        command.CommandText = bulder.ToString();
                                        rowsAffected = command.ExecuteNonQuery();
                                        logger.LogDebug("RowsAffected:" + rowsAffected);
                                        Console.WriteLine("RowsAffected:" + rowsAffected);
                                    }
                                }// end for


                                // Create SQL
                                command.Parameters.Clear();
                                bulder = new StringBuilder();
                                bulder.AppendFormat(" UPDATE ORG_DIST_CHANNEL   ");
                                bulder.AppendFormat(" SET [ACTIVE_FLAG]='N', [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME()  ");
                                bulder.AppendFormat(" WHERE ISNULL(SYNC_ID,-1)  != @VAL_SYNC_DATA_LOG_SEQ  ");
                                command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                // Execute SQL
                                logger.LogDebug("Query:" + bulder.ToString());
                                Console.WriteLine("Query:" + bulder.ToString());
                                command.CommandText = bulder.ToString();
                                rowsAffected = command.ExecuteNonQuery();
                                logger.LogDebug("RowsAffected:" + rowsAffected);
                                Console.WriteLine("RowsAffected:" + rowsAffected);



                                ///



                                //List<String> VAL_GROUP_Division = resData.Data..stream().collect(Collectors.groupingBy(r -> r.Division + "|" + r.Division_name));
                                List<string> VAL_GROUP_Division = new List<string>();
                                groupedBy = resultOutbound.Data.GroupBy(z => z.Division + "|" + z.Division_name);
                                foreach (var group in groupedBy)
                                {
                                    VAL_GROUP_Division.Add(group.Key);
                                }

                                //

                                foreach (string s in VAL_GROUP_Division)
                                {
                                    string[] G = s.Split("|");
                                    // INSERT SYNC_DATA_LOG
                                    // Create SQL
                                    command.Parameters.Clear();
                                    bulder = new StringBuilder();

                                    bulder.AppendFormat(" UPDATE ORG_DIVISION  ");
                                    bulder.AppendFormat(" SET [SYNC_ID]=@VAL_SYNC_DATA_LOG_SEQ, [DIVISION_NAME_TH]=@G1, [DIVISION_NAME_EN]=@G1, [ACTIVE_FLAG]='Y', [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                    bulder.AppendFormat(" WHERE [DIVISION_CODE]=@G0 ");
                                    command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                    command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                    command.Parameters.Add("@G1", SqlDbType.NVarChar);
                                    command.Parameters["@G1"].Value = G[1];
                                    command.Parameters.Add("@G0", SqlDbType.NVarChar);
                                    command.Parameters["@G0"].Value = G[0];
                                    QueryUtils.configParameter(command);

                                    // Execute SQL
                                    logger.LogDebug("Query:" + bulder.ToString());
                                    Console.WriteLine("Query:" + bulder.ToString());
                                    command.CommandText = bulder.ToString();
                                    rowsAffected = command.ExecuteNonQuery();
                                    logger.LogDebug("RowsAffected:" + rowsAffected);
                                    Console.WriteLine("RowsAffected:" + rowsAffected);

                                    if (rowsAffected == 0)
                                    {
                                        // Create SQL
                                        command.Parameters.Clear();
                                        bulder = new StringBuilder();

                                        bulder.AppendFormat(" INSERT INTO ORG_DIVISION ([DIVISION_CODE], [DIVISION_NAME_TH], [DIVISION_NAME_EN], [ACTIVE_FLAG], [SYNC_ID], [CREATE_USER], [CREATE_DTM], [UPDATE_USER], [UPDATE_DTM])  ");
                                        bulder.AppendFormat(" VALUES(@G0, @G1, @G1, 'Y', @VAL_SYNC_DATA_LOG_SEQ, 'SYSTEM', dbo.GET_SYSDATETIME(), 'SYSTEM', dbo.GET_SYSDATETIME()) ");
                                        command.Parameters.Add("@G0", SqlDbType.NVarChar);
                                        command.Parameters["@G0"].Value = G[0];
                                        command.Parameters.Add("@G1", SqlDbType.NVarChar);
                                        command.Parameters["@G1"].Value = G[1];
                                        command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                        command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                        QueryUtils.configParameter(command);
                                        // Execute SQL
                                        logger.LogDebug("Query:" + bulder.ToString());
                                        Console.WriteLine("Query:" + bulder.ToString());
                                        command.CommandText = bulder.ToString();
                                        rowsAffected = command.ExecuteNonQuery();
                                        logger.LogDebug("RowsAffected:" + rowsAffected);
                                        Console.WriteLine("RowsAffected:" + rowsAffected);
                                    }
                                }// end for


                                // Create SQL
                                command.Parameters.Clear();
                                bulder = new StringBuilder();
                                bulder.AppendFormat(" UPDATE ORG_DIVISION  ");
                                bulder.AppendFormat(" SET [ACTIVE_FLAG]='N', [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                bulder.AppendFormat(" WHERE ISNULL(SYNC_ID,-1)  != @VAL_SYNC_DATA_LOG_SEQ ");
                                command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                // Execute SQL
                                logger.LogDebug("Query:" + bulder.ToString());
                                Console.WriteLine("Query:" + bulder.ToString());
                                command.CommandText = bulder.ToString();
                                rowsAffected = command.ExecuteNonQuery();
                                logger.LogDebug("RowsAffected:" + rowsAffected);
                                Console.WriteLine("RowsAffected:" + rowsAffected);





                                ////////
                                foreach (Data data in resultOutbound.Data)
                                {
                                    // INSERT SYNC_DATA_LOG
                                    // Create SQL
                                    command.Parameters.Clear();
                                    bulder = new StringBuilder();

                                    bulder.AppendFormat(" UPDATE ORG_SALE_AREA  ");
                                    bulder.AppendFormat(" SET [SYNC_ID]=@VAL_SYNC_DATA_LOG_SEQ, [BUSS_AREA_CODE]=@Business_Area, [ACTIVE_FLAG]='Y', [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                    bulder.AppendFormat(" WHERE [ORG_CODE]=@Sale_Org and [CHANNEL_CODE]=@Distribution_Channel and [DIVISION_CODE]=@Division ");
                                    command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                    command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                    command.Parameters.Add("@Business_Area", SqlDbType.NVarChar);
                                    command.Parameters["@Business_Area"].Value = data.Business_Area;
                                    command.Parameters.Add("@Sale_Org", SqlDbType.NVarChar);
                                    command.Parameters["@Sale_Org"].Value = data.Sale_Org;
                                    command.Parameters.Add("@Distribution_Channel", SqlDbType.NVarChar);
                                    command.Parameters["@Distribution_Channel"].Value = data.Distribution_Channel;
                                    command.Parameters.Add("@Division", SqlDbType.NVarChar);
                                    command.Parameters["@Division"].Value = data.Division;
                                    QueryUtils.configParameter(command);

                                    // Execute SQL
                                    logger.LogDebug("Query:" + bulder.ToString());
                                    Console.WriteLine("Query:" + bulder.ToString());
                                    command.CommandText = bulder.ToString();
                                    rowsAffected = command.ExecuteNonQuery();
                                    logger.LogDebug("RowsAffected:" + rowsAffected);
                                    Console.WriteLine("RowsAffected:" + rowsAffected);

                                    if (rowsAffected == 0)
                                    {
                                        // Create SQL
                                        command.Parameters.Clear();
                                        bulder = new StringBuilder();

                                        bulder.AppendFormat(" INSERT INTO ORG_SALE_AREA ([AREA_ID], [ORG_CODE], [CHANNEL_CODE], [DIVISION_CODE], [BUSS_AREA_CODE], [ACTIVE_FLAG], [SYNC_ID], [CREATE_USER], [CREATE_DTM], [UPDATE_USER], [UPDATE_DTM])  ");
                                        bulder.AppendFormat(" VALUES(NEXT VALUE FOR ORG_SALE_AREA_SEQ, @Sale_Org, @Distribution_Channel, @Division, @Business_Area, 'Y', @VAL_SYNC_DATA_LOG_SEQ, 'SYSTEM', dbo.GET_SYSDATETIME(), 'SYSTEM', dbo.GET_SYSDATETIME()) ");
                                        command.Parameters.Add("@Sale_Org", SqlDbType.NVarChar);
                                        command.Parameters["@Sale_Org"].Value = data.Sale_Org;
                                        command.Parameters.Add("@Distribution_Channel", SqlDbType.NVarChar);
                                        command.Parameters["@Distribution_Channel"].Value = data.Distribution_Channel;
                                        command.Parameters.Add("@Division", SqlDbType.NVarChar);
                                        command.Parameters["@Division"].Value = data.Division;
                                        command.Parameters.Add("@Business_Area", SqlDbType.NVarChar);
                                        command.Parameters["@Business_Area"].Value = data.Business_Area;
                                        command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                        command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                        QueryUtils.configParameter(command);
                                        // Execute SQL
                                        logger.LogDebug("Query:" + bulder.ToString());
                                        Console.WriteLine("Query:" + bulder.ToString());
                                        command.CommandText = bulder.ToString();
                                        rowsAffected = command.ExecuteNonQuery();
                                        logger.LogDebug("RowsAffected:" + rowsAffected);
                                        Console.WriteLine("RowsAffected:" + rowsAffected);
                                    }
                                }// end for



                                // Create SQL
                                command.Parameters.Clear();
                                bulder = new StringBuilder();

                                bulder.AppendFormat(" UPDATE ORG_SALE_AREA  ");
                                bulder.AppendFormat(" SET [ACTIVE_FLAG]='N', [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                bulder.AppendFormat(" WHERE ISNULL(SYNC_ID,-1)  != @VAL_SYNC_DATA_LOG_SEQ ");
                                command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                // Execute SQL
                                logger.LogDebug("Query:" + bulder.ToString());
                                Console.WriteLine("Query:" + bulder.ToString());
                                command.CommandText = bulder.ToString();
                                rowsAffected = command.ExecuteNonQuery();
                                logger.LogDebug("RowsAffected:" + rowsAffected);
                                Console.WriteLine("RowsAffected:" + rowsAffected);





                                // Create SQL
                                command.Parameters.Clear();
                                bulder = new StringBuilder();
                                bulder.AppendFormat(" UPDATE SYNC_DATA_LOG  ");
                                bulder.AppendFormat(" SET [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                                bulder.AppendFormat(" WHERE [SYNC_ID]= @VAL_SYNC_DATA_LOG_SEQ ");
                                command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                                command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                                // Execute SQL
                                logger.LogDebug("Query:" + bulder.ToString());
                                Console.WriteLine("Query:" + bulder.ToString());
                                command.CommandText = bulder.ToString();
                                rowsAffected = command.ExecuteNonQuery();
                                logger.LogDebug("RowsAffected:" + rowsAffected);
                                Console.WriteLine("RowsAffected:" + rowsAffected);


                                // Attempt to commit the transaction.
                                transaction.Commit();
                                logger.LogDebug("Commit to database.");
                                Console.WriteLine("Commit to database.");
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug("Commit Exception Type: {0}", ex.GetType());
                        logger.LogDebug("  Message: {0} {1}", ex.Message, ex.ToString());
                        Console.WriteLine("Commit Exception Type: {0}", ex.GetType());
                        Console.WriteLine("  Message: {0} {1}", ex.Message, ex.ToString());

                        // Attempt to roll back the transaction.
                        if (transaction != null)
                        {
                            try
                            {
                                transaction.Rollback();
                            }
                            catch (Exception ex2)
                            {
                                // This catch block will handle any errors that may have occurred
                                // on the server that would cause the rollback to fail, such as
                                // a closed connection.
                                logger.LogDebug("Rollback Exception Type: {0}", ex2.GetType());
                                logger.LogDebug("  Message: {0} {1}", ex2.Message, ex2.ToString());
                                Console.WriteLine("Rollback Exception Type: {0}", ex2.GetType());
                                Console.WriteLine("  Message: {0} {1}", ex2.Message, ex2.ToString());
                            }
                        }

                        using (SqlConnection connection = new SqlConnection(connectionString_))
                        {
                            SqlCommand command = connection.CreateCommand();
                            // Create SQL
                            command.Parameters.Clear();
                            StringBuilder bulder = new StringBuilder();

                            bulder.AppendFormat(" UPDATE SYNC_DATA_LOG ");
                            bulder.AppendFormat(" SET [ERROR_DESC]=@ErrMsg, [UPDATE_USER]='SYSTEM', [UPDATE_DTM]=dbo.GET_SYSDATETIME() ");
                            bulder.AppendFormat(" WHERE [SYNC_ID]= @VAL_SYNC_DATA_LOG_SEQ ");
                            command.Parameters.Add("@ErrMsg", SqlDbType.NVarChar);
                            command.Parameters["@ErrMsg"].Value = (ex.Message + ":" + ex.ToString());
                            command.Parameters.Add("@VAL_SYNC_DATA_LOG_SEQ", SqlDbType.Decimal);
                            command.Parameters["@VAL_SYNC_DATA_LOG_SEQ"].Value = VAL_SYNC_DATA_LOG_SEQ;
                            // Execute SQL
                            SqlTransaction transn = null;
                            try
                            {
                                connection.Open();
                                // Start a local transaction.
                                transn = connection.BeginTransaction("T4");

                                // Must assign both transaction object and connection
                                // to Command object for a pending local transaction
                                command.Connection = connection;
                                command.Transaction = transn;
                                Int32 rowsAffected = command.ExecuteNonQuery();
                                logger.LogDebug("RowsAffected: {0}", rowsAffected);
                                Console.WriteLine("RowsAffected: {0}", rowsAffected);
                                // Attempt to commit the transaction.
                                transn.Commit();
                                logger.LogDebug("Commit to database.");
                                Console.WriteLine("Commit to database.");
                            }
                            catch (Exception ex3)
                            {
                                logger.LogDebug(ex3.Message+ex3.ToString());
                                Console.WriteLine(ex3.Message+ex3.ToString());
                                if (transn != null)
                                {
                                    transn.Rollback();
                                }
                            }
                        }
                        // SQL Scope
                        throw;
                    }
                    // SQL Scope
                }

            }

            responses = req.CreateResponse(HttpStatusCode.OK);
            return responses;
        }



    }
}
