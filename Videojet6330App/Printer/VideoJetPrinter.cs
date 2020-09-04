using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using Videojet6330App.Socket;

namespace Videojet6330App.Printer
{
    class VideoJetPrinter
    {
        private TcpSocketWrapper _socket;

        private const string PrinterName = "VideoJet6330";
        private const string DefaultSuccessResponse = "ACK\r";
        private const string DefaultFailureResponse = "ERR\r";

        public int MaxBufferCount {get; set;} = 150;
        public int MinBufferCount {get; set;} = 50;

        public async Task Connect(string host, int port)
        {
            try
            {
                _socket = new TcpSocketWrapper(host, port);
                await _socket.Connect(20);
            }
            catch (Exception exc) when (Handle(() => Log.Error(exc, $"[Printer {PrinterName}] Connect error"))) {}
        }

        public void Disconnect()
        {
            try
            {
                _socket.Disconnect();
            }
            catch (Exception exc) when (Handle(() => Log.Error(exc, $"[Printer {PrinterName}] Disconnect error"))) {}
        }

        public async Task<IEnumerable<string>> GetTemplates()
        {
            try
            {
                return await GetTemplateListCommand();
            }
            catch (Exception exc) when (Handle(() => Log.Error(exc, $"[Printer {PrinterName}] GetTemplates error")))
            {
                return Enumerable.Empty<string>();
            }
        }

        public async Task<int> GetBufferCount()
        {
            try
            {
                return await GetRecordCountCommand();
            }
            catch (Exception exc) when (Handle(() => Log.Error(exc, $"[Printer {PrinterName}] GetBufferCount error")))
            {
                return 0;
            }
        }

        public async Task ClearBuffer()
        {
            try
            {
                await ClearBufferCommand();
            }
            catch (Exception exc) when (Handle(() => Log.Error(exc, $"[Printer {PrinterName}] ClearBuffer error"))) {}
        }

        public async Task Init(string template)
        {
            try
            {
                var overallState = await WaitPrinterStarting();
                if (overallState == OverallState.Running)
                    await SetStateCommand(OverallState.Offline);
                var state = await GetStateCommand();
                var faults = await GetAllFaultsCommand();
                var errors = await GetAllWarningsCommand();

                //await ClearAllFaultsCommand();
                //await ClearAllWarningsCommand();

                //faults = await GetAllFaultsCommand();
                //errors = await GetAllWarningsCommand();

                //await DisableAllNotificationsCommand();
                // Установка шаблона и настроек буфера
                // Если не выбран шаблон печати, принтер не может перейти в статус Running
                await SelectTemplateCommand(template);
                var currentTemplate = await GetTemplateCommand();
                state = await GetStateCommand();
                if (state.CurrentJob != template)
                    throw SetException("Init", "invalid set current job", state.CurrentJob);

                var fields = await GetFieldsListCommand(template);
                var data = await GetCurrentDataCommand();

                await SetHeaderOnlyCommand(new[] {"Varfield00"});
                await SetHeaderOnlyCommand(fields.ToArray());

                await ClearBufferCommand();
                var count = await GetBufferCount();

                // Command not work
                //var max = await GetMaximumRecordsCommand();
                await SetMaximumRecordsCommand(MaxBufferCount);
				
                state = await GetStateCommand();
                if (state.OverallState == OverallState.Offline)
                    // Установка режима Running
                    await SetStateCommand(OverallState.Running);
                // Ожидание 1 секунды, чтобы принтер смог установить печатающую головку в положение печати
                await Task.Delay(TimeSpan.FromSeconds(3));
                state = await GetStateCommand();
            }
            catch (Exception exc) when (Handle(() => Log.Error(exc, $"[Printer {PrinterName}] Init error"))) {}
        }

        private async Task<OverallState> WaitPrinterStarting()
        {
            // Подробный порядок включения принтера
            // Разделы Appendix 1: State Transition Diagram и Example Code
            // документа CLARITY-Zipher Text Communications Protocol V 1.28.pdf
            // Включение     занимает 60 секунд (режим ShutDown, горит синий светодиод)
            // Инициализация занимает 15 секунд (режим StartingUp, мигает красный светодиод)
            // Неактивен                        (режим Offline)
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            do
            {
                //Console.WriteLine( $"{count++:D3}  {stopwatch.ElapsedMilliseconds}" );
                var state = await GetStateCommand();
                Log.Information($"[Printer {PrinterName}] " +
                                $"{stopwatch.Elapsed.Minutes:D2}.{stopwatch.Elapsed.Seconds:D2}.{stopwatch.Elapsed.Milliseconds:D4}" +
                                $" - {GetDescription(state.OverallState)}");
                if (state.OverallState == OverallState.Running || state.OverallState == OverallState.Offline)
                {
                    stopwatch.Stop();
                    return state.OverallState;
                }
                if (state.OverallState == OverallState.ShuttingDown)
                {
                    stopwatch.Stop();
                    throw SetException("Init", "printer is shuttingdown state");
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            } while (stopwatch.Elapsed < TimeSpan.FromSeconds(120));
            stopwatch.Stop();
            throw SetException("Init", "wait starting printer timeout");
        }

        private static bool Rethrow(Action action)
        {
            action();
            return false;
        }

        private static bool Handle(Action action)
        {
            action();
            return true;
        }

        public Task SwitchOn() => Task.CompletedTask;

        public async Task SwitchOff() => await SetStateCommand(OverallState.Offline);

        public async Task WriteNewCodes(IEnumerable<string> codes)
        {
            var enumerable = codes.ToList();
            Log.Information($"Write new codes = {enumerable.Count}");
            var date = DateTime.Now.ToString("dd.MM.yyyy");
            foreach (var code in enumerable)
            {
                Log.Information($"Write new code = {code}");
                var dict = new Dictionary<string, string> {{ "Varfield00", code}};
                await SetHeaderAndDataCommand(dict);    
            }

            try
            {
                await PrintCommand();
				await Task.Delay(TimeSpan.FromSeconds(5));
                await PrintCommand();
            }
            catch (Exception) { };

            // Тестовые данные
            var count = await GetRecordCountCommand();
            Log.Information($"Buffer count {count}");
            var freeSpace = await GetFreeSpaceCommand();
            Log.Information($"Free space {freeSpace}");
            var nextIndex = await GetNextRecordIndexCommand();
            Log.Information($"Next record index {nextIndex}");
            var lastIndex = await GetLastRecordIndexCommand();
            Log.Information($"Last record index {lastIndex}");

            await ClearBuffer();

            count = await GetRecordCountCommand();
            var state = await GetStateCommand();
            var faults = await GetAllFaultsCommand();
            var errors = await GetAllWarningsCommand();
        }

        #region Команды для работы с термотрансферным принтером Videojet Dataflex 6330

        #region Состояние термотрансферного принтера Videojet Dataflex 6330

        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
        private class CurrentState
        {
            public OverallState OverallState {get; set;}
            public ErrorState ErrorState {get; set;}
            public string CurrentJob {get; set;}
            public int BatchCount {get; set;}
            public int TotalCount {get; set;}
        }

        private enum OverallState
        {
            [Description("Выключен")] ShutDown = 0,
            [Description("Загружается")] StartingUp,
            [Description("Выключение")] ShuttingDown,
            [Description("Рабочий режим")] Running,
            [Description("Режим остановки")] Offline
        }

        private enum ErrorState
        {
            [Description("Нет ошибок")] NoErrors = 0,
            [Description("Предупреждение")] WarningsPresent,
            [Description("Ошибка")] FaultsPresent
        }

        private static string GetDescription(Enum value) =>
            value.GetType()
                 .GetMember(value.ToString())
                 .FirstOrDefault()
                ?.GetCustomAttribute<DescriptionAttribute>()
                ?.Description
         ?? value.ToString();

        #endregion Состояние термотрансферного принтера Videojet Dataflex 6330

        // ReSharper disable UnusedMember.Local
        private static string NameCommand(string command) =>
            command.Substring(0, 3) switch
            {
                "SEL" => "Job Select (data inserted into consecutive fields)",
                "SLA" => "Job Select (data inserted into named fields)",
                "SLI" => "Job Select with Allocation (data inserted into named fields)",
                "JDU" => "Job Data Update( data inserted into consecutive fields)",
                "JDA" => "Job Data Update( data inserted into named fields)",
                "JDI" => "Job Data Update with Allocation( data inserted into named fields)",
                "GJN" => "Gets the selected job name and line selection",
                "GJL" => "Get Job List",
                "GJF" => "Get Job Field List",
                "GJD" => "Get Current Job Data",
                "PRN" => "Print",
                "SST" => "Set printer state",
                "GST" => "Get printer state",
                "GFT" => "Gets the current Faults",
                "GWN" => "Gets the current Warnings",
                "CAF" => "Clear All Faults",
                "CAW" => "Clear All Warnings",
                "SHD" => "Serialization Header and Data",
                "SHO" => "Serialization Header Only",
                "SDO" => "Serialization Data Only",
                "SCF" => "Serialization Change Field Data",
                "SRC" => "Serialization Record Count",
                "SCB" => "Serialization Clear Buffer",
                "SID" => "Serialization Indexed Data",
                "SFS" => "Serialization Free Space",
                "SNI" => "Serialization Next Record Index to be printed",
                "SLR" => "Serialization Last Record Index printed",
                "SMR" => "Serialization Set Maximum number of Records",
                "DAN" => "The Disable All Notifications command",
                "SGM" => "Serialization Get Maximum number of Records",
                _ => $"Invalid command name {command}"
            };

        /// <summary>
        /// This command cause the selection of a job on the printer
        /// </summary>
        /// <param name="template"></param>
        /// <param name="param"></param>
        /// <returns>
        /// On success, return the default success response.
        /// On failure, the default failure response is returned.
        /// If the command succeeds, the response is sent immediately.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> SelectTemplateCommand(string template, IEnumerable<string> param = null)
        {
            var query = $"SEL|{template}|" +
                        param?.Aggregate(string.Empty, (current, item) => current + item + "|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command cause the selection of a job on the printer
        /// </summary>
        /// <param name="template"></param>
        /// <param name="param"></param>
        /// <returns>
        /// On success, return the default success response.
        /// On failure, the default failure response is returned.
        /// If the command succeeds, the response is sent immediately.
        /// </returns>
        private async Task<string> SelectTemplateWithFieldNamesCommand(string template,
            Dictionary<string, string> param = null)
        {
            var query = $"SLA|{template}|" +
                        param?.Aggregate(string.Empty, (current, item) => current + $"{item.Key}={item.Value}|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command cause the selection of a job on the printer
        /// </summary>
        /// <param name="template"></param>
        /// <param name="allocation"></param>
        /// <param name="param"></param>
        /// <returns>
        /// On success, returns an ID for the item placed in  the  job  queue  by  this  command.
        /// On failure, the default failure response is returned.
        /// If the command succeeds, the response is sent immediately.
        /// </returns>
        private async Task<string> SelectTemplateWithAllocationCommand(string template, string allocation,
            Dictionary<string, string> param = null)
        {
            var query = $"SLI|{template}|{allocation}|" +
                        param?.Aggregate(string.Empty, (current, item) => current + $"{item.Key}={item.Value}|") + "\r";
            return await ExecuteRequest(query, @"^\d+\r$");
        }

        /// <summary>
        /// This command cause the variable fields on the currently selected job to be updated.
        /// </summary>
        /// <param name="param"></param>
        /// <returns>
        /// On success, return the default success response.
        /// On failure, the default failure response is returned.
        /// If the command succeeds, the response is sent immediately.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> UpdateDataTemplateCommand(IEnumerable<string> param = null)
        {
            var query = "JDU|" +
                        param?.Aggregate(string.Empty, (current, item) => current + item + "|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command cause the variable fields on the currently selected job to be updated.
        /// </summary>
        /// <param name="param"></param>
        /// <returns>
        /// On success, return the default success response.
        /// On failure, the default failure response is returned.
        /// If the command succeeds, the response is sent immediately.
        /// </returns>
        private async Task<string> UpdateDataTemplateWithFieldNamesCommand(Dictionary<string, string> param = null)
        {
            var query = "JDA|" +
                        param?.Aggregate(string.Empty, (current, item) => current + $"{item.Key}={item.Value}|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command cause the variable fields on the currently selected job to be updated.
        /// </summary>
        /// <param name="allocation"></param>
        /// <param name="param"></param>
        /// <returns>
        /// On success, returns an ID for the item placed in  the  job  queue  by  this  command.
        /// On failure, the default failure response is returned.
        /// If the command succeeds, the response is sent immediately.
        /// </returns>
        private async Task<string> UpdateDataTemplateWithAllocationCommand(string allocation,
            Dictionary<string, string> param = null)
        {
            var query = $"JDI|{allocation}|" +
                        param?.Aggregate(string.Empty, (current, item) => current + $"{item.Key}={item.Value}|") + "\r";
            return await ExecuteRequest(query, @"^\d+\r$");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>
        /// On success, returns JOB|<jobname/>|<linenumber/>|<CR/> 
        /// Line number is set to a dash "-" if the printer is not currently in line select mode.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<string> GetTemplateCommand()
        {
            var response = await ExecuteRequest("GJN\r", @"^JOB\|.+\|.+\|\r$");
            return response.Split('|')[1];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>
        /// On success, returns JBL|<count/>|[<jobname/>|]<CR/>  
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<IEnumerable<string>> GetTemplateListCommand() =>
            await ExecuteRequestList("GJL\r", @"^JBL\|\d+\|.*\r$");

        /// <summary>
        /// 
        /// </summary>
        /// <param name="template"></param>
        /// <returns>
        /// On success, returns JFL|<count/>|[<fieldname/>|]<CR/> 
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<IEnumerable<string>> GetFieldsListCommand(string template) =>
            await ExecuteRequestList($"GJF|{template}|\r", @"^JFL\|\d+\|.*\r$");

        /// <summary>
        /// 
        /// </summary>
        /// <returns>
        /// On success, returns JDL|<count/>|[<fieldname/>=<value/>|]<CR/>
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<Dictionary<string, string>> GetCurrentDataCommand()
        {
            const string query = "GJD\r";
            var response = await ExecuteRequestList(query, @"^JDL\|\d+\|.*\r$");

            var dict = new Dictionary<string, string>();
            foreach (var value in response.Select(item => item.Split("=")))
            {
                if (value.Length != 2)
                    throw SetException(query, "invalid format response", string.Join(",", value));
                dict.Add(value[0], value[1]);
            }
            return dict;
        }

        /// <summary>
        /// This command causes the currently selected job to be printed once. 
        /// </summary>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// If the command succeeds, the response is sent after the job has been printed.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> PrintCommand() => await ExecuteRequest("PRN\r");

        /// <summary>
        /// This command attempts to set the overall state of the printer. 
        /// </summary>
        /// <param name="desiredState"></param>
        /// <remarks>
        /// State Transition Diagram
        /// ShutDown     -> StartingUp
        /// StartingUp   -> Offline
        /// Offline      -> Running, ShuttingDown
        /// Running      -> Offline, ShuttingDown
        /// ShuttingDown -> ShutDown
        /// </remarks>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// If the command succeeds, the response is sent after the state transition has taken place.
        /// </returns>
        // ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> SetStateCommand(OverallState desiredState) =>
            await ExecuteRequest($"SST|{(int) desiredState}|\r");

        /// <summary>
        /// This request retrieves various state values from the printer. 
        /// </summary>
        /// <returns>
        /// The GST request retrieves the following values: 
        /// overall state The overall state of the printer, as described for the SST command.
        /// error state   The error condition of the printer. This will be one of the following: 
        /// current job   The job selected in the printer.This will be empty if no job is selected.
        /// batch count   The printer’s batch count.
        /// total count   The printer’s total count.
        /// </returns>
        private async Task<CurrentState> GetStateCommand()
        {
            const string query = "GST\r";
            var response = await ExecuteRequest(query, @"^STS\|[0-4]{1}\|[0-2]{1}\|.*\|\d+\|\d+\|\r$");

            var array = response.Split('|').Skip(1).SkipLast(1).ToList();
            if (array.Count != 5)
                throw SetException(query, "invalid format response", response);
            if (!int.TryParse(array[0], out var overallState) ||
                !int.TryParse(array[1], out var errorState) ||
                !int.TryParse(array[3], out var batchCount) ||
                !int.TryParse(array[4], out var totalCount))
                throw SetException(query, "invalid convert string to int", response);

            Log.Information(
                $"Get state {GetDescription((OverallState) overallState)}, {GetDescription((ErrorState) errorState)}, {array[2]}");
            return new CurrentState
            {
                OverallState = (OverallState) overallState,
                ErrorState = (ErrorState) errorState,
                CurrentJob = array[2],
                BatchCount = batchCount,
                TotalCount = totalCount
            };
        }

        /// <summary>
        /// This command lists all current faults in the printer.
        /// </summary>
        /// <returns>
        /// On success, returns the total count of the number of faults followed by a list of current faults.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<IEnumerable<string>> GetAllFaultsCommand()
        {
            var response = await ExecuteRequestList("GFT\r", @"^FLT\|\d+\|.*\r$", 3);

            var result = new List<string>();
            var array = response.ToList();

            for (var i = 0; i < array.Count; i += 3)
            {
                var str = array[i] + " " +
                          (array[i + 1] == "0" ? "Not Cleanable" : "Cleanable") + " " +
                          array[i + 2];
                result.Add(str);
            }
            return result;
        }

        /// <summary>
        /// This command lists all current warnings in the printer. 
        /// </summary>
        /// <returns>
        /// On  success,  returns  the  total  count  of  the  number  of  warnings  followed  by  a  list  of  current warnings.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<IEnumerable<string>> GetAllWarningsCommand()
        {
            var response = await ExecuteRequestList("GWN\r", @"^WRN\|\d+\|.*\r$", 3);

            var result = new List<string>();
            var array = response.ToList();
            for (var i = 0; i < array.Count; i += 3)
            {
                var str = array[i] + " " +
                          (array[i + 1] == "0" ? "Not Cleanable" : "Cleanable") + " " +
                          array[i + 2];
                result.Add(str);
            }
            return result;
        }

        /// <summary>
        /// This command attempts to clear all fault conditions present in the printer. 
        /// </summary>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// If the command succeeds, the response is sent after all faults have been cleared.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> ClearAllFaultsCommand() => await ExecuteRequest("CAF\r");

        /// <summary>
        /// This command attempts to clear all warning conditions present in the printer. 
        /// </summary>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// If the command succeeds, the response is sent after all warnings have been cleared.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> ClearAllWarningsCommand() => await ExecuteRequest("CAW\r");

        /// <summary>
        /// This commands sends a single record with both field names and data.
        /// </summary>
        /// <param name="param"></param>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<string> SetHeaderAndDataCommand(Dictionary<string, string> param)
        {
            var query = "SHD|" + param?.Aggregate(string.Empty,
                            (current, item) => current + $"{item.Key}={item.Value}|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command sends field names only. 
        /// </summary>
        /// <param name="fieldNames"></param>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> SetHeaderOnlyCommand(IEnumerable<string> fieldNames)
        {
            var query = "SHO|" + fieldNames?.Aggregate(string.Empty, (current, item) => current + $"{item}|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command sends field data only.
        /// </summary>
        /// <param name="records"></param>
        /// <returns>
        /// On success, returns SFS|<s/>|<CR/>, where <s/> is the number of available bytes in the serialization buffer.
        /// On failure, returns the default failure response.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> SetDataOnlyCommand(IEnumerable<string> records)
        {
            var query = "SDO|" + records?.Aggregate(string.Empty, (current, item) => current + $"{item}|") + "\r";
            return await ExecuteRequest(query, @"^SFS\|\d+\|\r$");
        }

        /// <summary>
        /// This command is used to update non-serialization fields when in serialization mode.
        /// </summary>
        /// <param name="param"></param>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.  
        /// </returns>
        private async Task<string> ChangeFieldDataCommand(Dictionary<string, string> param)
        {
            var query = "SCF|" + param?.Aggregate(string.Empty,
                            (current, item) => current + $"{item.Key}={item.Value}|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command gets the number of records currently in the serialization buffer. 
        /// </summary>
        /// <returns>
        /// On success, returns SRC|<c/>|<CR/>, where <c/> is the number of records in the serialization buffer.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<int> GetRecordCountCommand() => await ExecuteRequestInt("SRC\r");

        /// <summary>
        /// This command clears the serialization buffer. 
        /// </summary>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> ClearBufferCommand() => await ExecuteRequest("SCB\r");

        /// <summary>
        /// This command adds another record with the supplied data and the supplied ID/serial number. 
        /// </summary>
        /// <param name="recordIndex"></param>
        /// <param name="data"></param>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<string> SetDataWithIndexCommand(int recordIndex, IEnumerable<string> data)
        {
            var query = $"SID|{recordIndex}|" +
                        data?.Aggregate(string.Empty, (current, item) => current + item + "|") + "\r";
            return await ExecuteRequest(query);
        }

        /// <summary>
        /// This command gets the amount of free space in the serialization buffer. 
        /// </summary>
        /// <returns>
        /// On success, returns SFS|<s/>|<CR/>, <s/> is the number of available bytes in the serialization buffer.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<int> GetFreeSpaceCommand() => await ExecuteRequestInt("SFS\r");

        /// <summary>
        /// This command gets the next index to be printed. 
        /// </summary>
        /// <returns>
        /// On success, returns SNI|<i/>|<CR/>, <i/> is the next index to be printed.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<int> GetNextRecordIndexCommand() => await ExecuteRequestInt("SNI\r");

        /// <summary>
        /// This command gets the last index printed. 
        /// </summary>
        /// <returns>
        /// On success, returns SLR|<i/>|<CR/>, <i/> is the last index printed.
        /// On failure, returns the default failure response.
        /// </returns>
        private async Task<int> GetLastRecordIndexCommand() => await ExecuteRequestInt("SLR\r");

        /// <summary>
        /// This command sets the maximum number of records allowed in the serialization buffer.
        /// </summary>
        /// <param name="count"></param>
        /// <returns>
        /// On success, returns the default success response.
        /// On failure, returns the default failure response.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> SetMaximumRecordsCommand(int count) => await ExecuteRequest($"SMR|{count}|\r");

        /// <summary>
        /// This command gets the maximum number of records allowed in the serialization buffer. 
        /// </summary>
        /// <returns>
        /// On success, returns SGM|<r/>|<CR/>, where <r/> is the maximum number of records allowed in the serialization buffer.
        /// On failure, returns the default failure response
        /// </returns>
        private async Task<int> GetMaximumRecordsCommand() => await ExecuteRequestInt("SMG\r");

        /// <summary>
        /// This command switches off all Async comms messages and prevents the sending of all notifications. Equivalent to SAN|0|
        /// </summary>
        /// <returns>
        /// On success, return the default success response.
        /// On failure, the default failure response is returned.
        /// </returns>
        /// ReSharper disable once UnusedMethodReturnValue.Local
        private async Task<string> DisableAllNotificationsCommand() => await ExecuteRequest("DAN\r");

        private async Task<string> ExecuteRequest(string query, string pattern = DefaultSuccessResponse)
        {
            var response = await _socket.Request("\r" + query, 20);

            if (0 == string.Compare(response, DefaultFailureResponse, StringComparison.OrdinalIgnoreCase))
                throw SetException(query, "completed with default error", response);

            if (0 == string.Compare(pattern, DefaultSuccessResponse, StringComparison.OrdinalIgnoreCase))
            {
                if (0 == string.Compare(response, pattern, StringComparison.OrdinalIgnoreCase))
                    return response;

                throw SetException(query, "completed with error", response);
            }

            if (!Regex.IsMatch(response, pattern, RegexOptions.IgnoreCase))
                throw SetException(query, "completed with regex is not match", response);
            return response;
        }

        private async Task<IEnumerable<string>> ExecuteRequestList(string query, string pattern, int ratio = 1)
        {
            var response = await ExecuteRequest(query, pattern);

            var array = response.Split('|').Skip(1).SkipLast(1).ToList();
            if (array.Count == 0)
                throw SetException(query, "empty response", response);
            if (!int.TryParse(array[0], out var count))
                throw SetException(query, "invalid convert string to int", array[0]);
            if (count * ratio != array.Count - 1)
                throw SetException(query, "invalid count data", response);
            return count == 0 ? Enumerable.Empty<string>() : array.Skip(1);
        }

        private async Task<int> ExecuteRequestInt(string query, int index = 0)
        {
            if (query.EndsWith('\r'))
                query = query.TrimEnd('\r');

            var pattern = $@"^{query}\|.+\|\r$";
            var response = await ExecuteRequest(query + "\r", pattern);

            var array = response.Split('|').Skip(1).SkipLast(1).ToList();
            if (array.Count <= index)
                throw SetException(query, "empty response", response);

            if (!int.TryParse(array[index], out var count))
                throw SetException(query, "invalid convert string to int", array[index]);
            return count;
        }

        private static Exception SetException(string query, string description, string response = "") =>
            new InvalidOperationException(
                string.IsNullOrWhiteSpace(response)
                    ? $"{PrinterName} command \"{NameCommand(query)}\" {description}"
                    : $"{PrinterName} command \"{NameCommand(query)}\" {description} - {response}");

        // ReSharper restore UnusedMember.Local

        #endregion Команды для работы с термотрансферным принтером Videojet Dataflex 6330
    }
}