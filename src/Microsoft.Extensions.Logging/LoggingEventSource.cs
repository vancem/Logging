
// #define NO_EVENTSOURCE_COMPLEX_TYPE_SUPPORT
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;
using System.Threading;

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// The LoggingEventSource is the bridge form all ILogger based logging to EventSource/EventListener logging.
    /// 
    /// You turn this logging on by enabling the EvenSource called
    /// 
    ///      Microsoft-Extensions-Logging
    ///  
    /// When you enabled the EventSource, the EventLevel you set is translated in the obvious way to the level
    /// associated with the ILogger (thus Debug = verbose, Informational = Informational ... Critical == Critical)
    /// 
    /// This allows you to filter by event level in a straighforward way.   
    /// 
    /// For finer control you can specify a EventSource Argument called  
    /// 
    /// FilterSpecs 
    /// 
    /// The FilterSpecs argument is a semicolon separated list of specifications.   Where each specification is
    /// 
    /// SPEC =                          // empty spec, same as *
    ///      | NAME                     // Just a name the level is the default level 
    ///      | NAME : LEVEL            // specifies level for a particular logger (can have a * suffix).  
    ///      
    /// Where Name is the name of a ILoggger (case matters), Name can have a * which acts as a wildcard 
    /// AS A SUFFIX.   Thus Net* will match any loggers that start with the 'Net'.   
    /// 
    /// The LEVEL is a number or a LogLevel string. 0=Trace, 1=Debug, 2=Information, 3=Warning,  4=Error, Critical=5
    /// This speicifies the level for the associated pattern.  If the number is not specified, (first form 
    /// of the specification) it is the default level for the EventSource.   
    /// 
    /// First match is used if a partciular name matches more than one pattern.   
    ///
    /// In addition the level and FilterSpec argument, you can also set EventSource Keywords.  See the Keywords
    /// definition below, but basically you get to decide if you wish to have 
    /// 
    ///   * Keywords.Message - You get the event with the data in parsed form.
    ///   * Keywords.JsonMessage - you get an event with the data in parse form but as a JSON blob (not broken up by argument ...)
    ///   * Keywords.FormattedMessage - you get an event with the data formatted as a string
    ///
    /// It is expected that you will turn only one of these keywords on at a time, but you can turn them all on (and get
    /// the same data logged three different ways.  
    /// 
    /// Example Usage 
    /// 
    /// This example shows how to use an EventListener to get ILogging information
    /// 
    /// class MyEventListener : EventListener {
    ///     protected override void OnEventSourceCreated(EventSource eventSource) {
    ///         if (eventSource.Name == "Microsoft-Extensions-Logging") {
    ///             // initialize a string, string dictionary of arguments to pass to the EventSource.  
    ///             // Turn on loggers matching App* to Information, everything else (*) is the default level (which is EventLevel.Error)
    ///             var args = new Dictionary&lt;string, string&gt;() { { "FilterSpecs", "App*:Information;*" } };
    ///             // Set the default level (verbosity) to Error, and only ask for the formatted messages in this case.  
    ///             EnableEvents(eventSource, EventLevel.Error, LoggingEventSource.Keywords.FormattedMessage, args);
    ///         }
    ///     }
    ///     protected override void OnEventWritten(EventWrittenEventArgs eventData) {
    ///         // Look for the formatted message event, which has the following argument layout (as defined in the LoggingEventSource.  
    ///         // FormattedMessage(LogLevel Level, int FactoryID, string LoggerName, string EventId, string FormattedMessage);
    ///         if (eventData.EventName == "FormattedMessage")
    ///             Console.WriteLine("Logger {0}: {1}", eventData.Payload[2], eventData.Payload[4]);
    ///     }
    /// }
    /// </summary>
    [EventSource(Name = "Microsoft-Extensions-Logging")]
    internal class LoggingEventSource : EventSource
    {
        /// <summary>
        /// This is public from an EventSource consumer point of view, but since these defintions 
        /// are not needed outside this class 
        /// </summary>
        public class Keywords
        {
            /// <summary>
            /// Meta events are evnets about the LoggingEventSource itself (that is they did not come from ILogger
            /// </summary>
            public const EventKeywords Meta = (EventKeywords)1;
            /// <summary>
            /// Turns on the 'Message' event when ILogger.Log() is called.   It gives the information in a programatic (not formatted) way  
            /// </summary>
            public const EventKeywords Message = (EventKeywords)2;
            /// <summary>
            /// Turns on the 'FormatMessage' event when ILogger.Log() is called.  It gives the formatted string version of the information.  
            /// </summary>
            public const EventKeywords FormattedMessage = (EventKeywords)4;
            /// <summary>
            /// Turns on the 'MessageJson' event when ILogger.Log() is called.   It gives  JSON representation of the Arguments. 
            /// </summary>
            public const EventKeywords JsonMessage = (EventKeywords)8;
        }

        /// <summary>
        ///  The one and only instance of the LoggingEventSource.  
        /// </summary>
        public static readonly LoggingEventSource Logger = new LoggingEventSource();

        /// <summary>
        /// This is the one public method in this class.   It should be called when the LoggerFactory is created.   
        /// This allows The LoggingEventSource to wire itself into that LoggerFactory and pass on any events to any
        /// consumers of the LoggingEventSource.  
        /// </summary>
        [NonEvent]
        public void LoggerFactoryCreated(LoggerFactory factory)
        {
            lock (this)
            {
                var newLoggerProvider = new EventSourceLoggerProvider(factory, _loggingProviders);
                _loggingProviders = newLoggerProvider;

                // If the EventSource has already been turned on.  set the filters.  
                if (_filterSpec != null)
                    newLoggerProvider.SetFilterSpec(_filterSpec);
            }
        }

        /// <summary>
        /// FormattedMessage() is called when ILogger.Log() is called. and the FormattedMessage keyword is active
        /// This only gives you the human reasable formatted message. 
        /// </summary>
        [Event(1, Keywords = Keywords.FormattedMessage, Level = EventLevel.LogAlways)]
        private void FormattedMessage(LogLevel Level, int FactoryID, string LoggerName, string EventId, string FormattedMessage)
        {
            WriteEvent(1, Level, FactoryID, LoggerName, EventId, FormattedMessage);
        }

#if !NO_EVENTSOURCE_COMPLEX_TYPE_SUPPORT

        // Events for the EventSource.  Note that these are public from a consumer point of view
        // but are marked private because they don't need to be called from outside this class.  
        /// <summary>
        /// Message() is called when ILogger.Log() is called. and the Message keyword is active
        /// This gives you the logged information in a programatic format (arguments are key-value pairs)
        /// </summary>
        [Event(2, Keywords = Keywords.Message, Level = EventLevel.LogAlways)]
        private void Message(LogLevel Level, int FactoryID, string LoggerName, string EventId, ExceptionInfo Exception, IEnumerable<KeyValuePair<string, string>> Arguments)
        {
            WriteEvent(2, Level, FactoryID, LoggerName, EventId, Exception, Arguments);
        }
#endif

        /// <summary>
        /// ActivityStart is called when ILogger.BeginScope() is called   
        /// </summary>
        [Event(3, Keywords = Keywords.Message | Keywords.FormattedMessage, Level = EventLevel.LogAlways, ActivityOptions = EventActivityOptions.Recursive)]
#if !NO_EVENTSOURCE_COMPLEX_TYPE_SUPPORT
        private void ActivityStart(int ID, int FactoryID, string LoggerName, IEnumerable<KeyValuePair<string, string>> Arguments)
        {
            WriteEvent(3, ID, FactoryID, LoggerName, Arguments);
        }

        [Event(4, Keywords = Keywords.Message | Keywords.FormattedMessage, Level = EventLevel.LogAlways)]
        private void ActivityStop(int ID, int FactoryID, string LoggerName)
        {
            WriteEvent(4, ID, FactoryID, LoggerName);
        }
#endif

        [Event(5, Keywords = Keywords.JsonMessage, Level = EventLevel.LogAlways)]
        private void MessageJson(LogLevel Level, int FactoryID, string LoggerName, string EventId, string ExceptionJson, string ArgumentsJson)
        {
            WriteEvent(5, Level, FactoryID, LoggerName, EventId, ExceptionJson, ArgumentsJson);
        }

        [Event(6, Keywords = Keywords.JsonMessage | Keywords.FormattedMessage, Level = EventLevel.LogAlways)]
        private void ActivityJsonStart(int ID, int FactoryID, string LoggerName, string ArgumentsJson)
        {
            WriteEvent(6, ID, FactoryID, LoggerName, ArgumentsJson);
        }

        [Event(7, Keywords = Keywords.JsonMessage | Keywords.FormattedMessage, Level = EventLevel.LogAlways)]
        private void ActivityJsonStop(int ID, int FactoryID, string LoggerName)
        {
            WriteEvent(7, ID, FactoryID, LoggerName);
        }

        /// <summary>
        /// ExceptionInfo is the serialized form of an exception.   
        /// </summary>
        [EventData]
        private class ExceptionInfo
        {
            public string TypeName;
            public string Message;
            public int HResult;
            public string VerboseMessage;       // This is the ToString() of the Exception
        }

        #region private 
        /// <summary>
        /// Converts a keyvalue bag to JSON.  
        /// </summary>
        private static string ToJson(IEnumerable<KeyValuePair<string, string>> keyValues)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            bool first = true;
            foreach (var keyValue in keyValues)
            {
                if (!first)
                    sb.Append(',').AppendLine();
                first = false;

                sb.Append('"').Append(keyValue.Key).Append("\":\"");

                // Write out the value characters, escaping things as needed.  
                foreach (var c in keyValue.Value)
                {
                    if (Char.IsControl(c))
                    {
                        if (c == '\n')
                            sb.Append("\\n");
                        else if (c == '\r')
                            sb.Append("\\r");
                        else
                            sb.Append("\\u").Append(((int)c).ToString("x").PadLeft(4, '0'));
                    }
                    else
                    {
                        if (c == '"' || c == '\\')
                            sb.Append('\\');
                        sb.Append(c);
                    }
                }
                sb.Append('"');     // Close the string.  
            }
            sb.AppendLine().AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// EventSourceLoggerProvider is used to connect the LoggerFactory to the LoggingEventSource.   
        /// Its main job is to simply create new EvnetSourceLogger for every
        /// compontent name (passed to CreateLogger). 
        /// </summary>
        internal class EventSourceLoggerProvider : ILoggerProvider
        {
            /// <summary>
            /// Create a new ILoggerProvider that will be added to the LoggerFactory 'factory'.  This however
            /// does NOT happen until the first SetFilterSpec which is non-null (thus we don't subscribe until
            /// some EventSource consumer has asked for some data).  
            /// </summary>
            public EventSourceLoggerProvider(LoggerFactory factory, EventSourceLoggerProvider next = null)
            {
                LoggerFactory = factory;
                Next = next;
                _eventSource = LoggingEventSource.Logger;
            }

            /// <summary>
            /// The LoggerFactory (which represents a single tenant in a multi-tenant scenario), associated with this LoggerProvider
            /// </summary>
            public LoggerFactory LoggerFactory { get; private set; }

            /// <summary>
            /// EventSourceLoggerProvider are a linked list.  
            /// </summary>
            public EventSourceLoggerProvider Next;

            /// <summary>
            /// A small integer that uniquely identifies the LoggerFactory assoicated with this LoggingProvider.
            /// Zero is illegal (it means we are uninitialized), and have to be added to the factory. 
            /// </summary>
            public int FactoryID;

            // Sets the filtering for a particular 
            public void SetFilterSpec(string filterSpec)
            {
                _filterSpec = filterSpec;
                _defaultLevel = GetDefaultLevel(_eventSource);

                // Update the levels of all the loggers to match what the filter specification asks for.   
                for (var logger = _loggers; logger != null; logger = logger.Next)
                    ParseLevelSpecs(filterSpec, _defaultLevel, logger.Name, out logger.Level);

                if (filterSpec != null && FactoryID == 0)
                {
                    // Compute an ID for the Factory.  It is its position in the list (starting at 1, we reserve 0 to mean unstarted). 
                    FactoryID = 1;
                    for (var cur = Next; cur != null; cur = cur.Next)
                        FactoryID++;

                    // Add myself to the factory.  Now my CreateLogger methods will be called.  
                    LoggerFactory.AddProvider(this);
                }
            }

            /// <summary>
            /// Silly, what we really want is to get the EventLevel from the EventSource 
            /// </summary>
            /// <param name="eventSource"></param>
            /// <returns></returns>
            private static LogLevel GetDefaultLevel(LoggingEventSource eventSource)
            {
                if (eventSource.IsEnabled(EventLevel.Informational, Keywords.Message | Keywords.FormattedMessage | Keywords.JsonMessage))
                {
                    if (eventSource.IsEnabled(EventLevel.Verbose, Keywords.Message | Keywords.FormattedMessage | Keywords.JsonMessage))
                        return LogLevel.Debug;
                    else
                        return LogLevel.Information;
                }
                else
                {
                    if (eventSource.IsEnabled(EventLevel.Warning, Keywords.Message | Keywords.FormattedMessage | Keywords.JsonMessage))
                        return LogLevel.Warning;
                    else
                    {
                        if (eventSource.IsEnabled(EventLevel.Error, Keywords.Message | Keywords.FormattedMessage | Keywords.JsonMessage))
                            return LogLevel.Error;
                        else
                            return LogLevel.Critical;
                    }
                }
            }

            public ILogger CreateLogger(string categoryName)
            {
                var newLogger = _loggers = new EventSourceLogger(categoryName, this, _loggers);
                ParseLevelSpecs(_filterSpec, _defaultLevel, newLogger.Name, out newLogger.Level);
                return newLogger;
            }


            public void Dispose()
            {
                SetFilterSpec(null);        // Turn off any logging.  
            }

            #region private 

            /// <summary>
            /// Given a set of specifications  Pat1:Level1;Pat1;Level2 ... Where
            /// Pat is a string pattern (a logger Name with a optional trailing wildcard * char)
            /// and Level is a number 0 (Trace) through 5 (Critical).  
            /// 
            /// The :Level can be omitted (thus Pat1;Pat2 ...) in which case the level is 1 (Debug). 
            /// 
            /// A completely emtry sting act like * (all loggers set to Debug level).  
            /// 
            /// The first speciciation that 'loggers' Name matches is used.   
            /// </summary>
            private static void ParseLevelSpecs(string filterSpec, LogLevel defaultLevel, string loggerName, out LogLevel level)
            {
                if (filterSpec == null)
                {
                    level = LogLevel.None + 1;      // Null means disable.  
                    return;
                }
                if (filterSpec == "")
                {
                    level = defaultLevel;
                    return;
                }
                level = LogLevel.None + 1;   // If the logger does not match something, it is off. 

                // See if logger.Name  matches a _filterSpec pattern.  
                int namePos = 0;
                int specPos = 0;
                for (;;)
                {
                    if (namePos < loggerName.Length)
                    {
                        if (filterSpec.Length <= specPos)
                            break;
                        char specChar = filterSpec[specPos++];
                        char nameChar = loggerName[namePos++];
                        if (specChar == nameChar)
                            continue;

                        // We allow wildcards a the end.  
                        if (specChar == '*' && ParseLevel(defaultLevel, filterSpec, specPos, ref level))
                            return;
                    }
                    else if (ParseLevel(defaultLevel, filterSpec, specPos, ref level))
                        return;

                    // Skip to the next spec in the ; separated list.  
                    specPos = filterSpec.IndexOf(';', specPos) + 1;
                    if (specPos <= 0) // No ; done. 
                        break;
                    namePos = 0;    // Reset where we are searching in the name.  
                }
            }

            /// <summary>
            /// Parses the level specification (which should look like :N where n is a  number 0 (Trace)
            /// through 5 (Critical).   It can also be an empty string (which means 1 (Debug) and ';' marks 
            /// the end of the specifcation This specification should start at spec[curPos]
            /// It returns the value in 'ret' and returns true if successful.  If false is returned ret is left unchanged.  
            /// </summary>
            private static bool ParseLevel(LogLevel defaultLevel, string spec, int specPos, ref LogLevel ret)
            {
                int endPos = spec.IndexOf(';', specPos);
                if (endPos < 0)
                    endPos = spec.Length;

                if (specPos == endPos)
                {
                    // No :Num spec means Debug
                    ret = defaultLevel;
                    return true;
                }
                if (spec[specPos++] != ':')
                    return false;

                string levelStr = spec.Substring(specPos, endPos - specPos);
                int level;
                switch(levelStr)
                {
                    case "Trace":
                        ret = LogLevel.Trace;
                        break;
                    case "Debug":
                        ret = LogLevel.Debug;
                        break;
                    case "Information":
                        ret = LogLevel.Information;
                        break;
                    case "Warning":
                        ret = LogLevel.Warning;
                        break;
                    case "Error":
                        ret = LogLevel.Error;
                        break;
                    case "Critical":
                        ret = LogLevel.Critical;
                        break;
                    default:
                        if (!int.TryParse(levelStr, out level))
                            return false;
                        if (!(LogLevel.Trace <= (LogLevel)level && (LogLevel)level <= LogLevel.None))
                            return false;
                        ret = (LogLevel)level;
                        break;
                }
                return true;
            }

            /// <summary>
            /// EventSourceLogger hooks up to the ILogger framwork and implements the Ilogger.Log() method
            /// that forwards the messages to the eventSource.   
            /// </summary>
            private class EventSourceLogger : ILogger
            {
                /// <summary>
                /// Creates a Logger that is off 
                /// </summary>
                public EventSourceLogger(string name, EventSourceLoggerProvider provider, EventSourceLogger next)
                {
                    Provider = provider;
                    Name = name;
                    Level = LogLevel.None + 1;              // Turn off loggging.  
                    Next = next;
                }

                public readonly string Name;
                public readonly EventSourceLoggerProvider Provider;
                public LogLevel Level;
                public readonly EventSourceLogger Next;     // Part of a linked list

                public bool IsEnabled(LogLevel logLevel)
                {
                    return logLevel >= Level;
                }

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                {
                    if (!IsEnabled(logLevel))
                        return;

                    // See if they want the formatted message
                    if (Provider._eventSource.IsEnabled(System.Diagnostics.Tracing.EventLevel.Critical, LoggingEventSource.Keywords.FormattedMessage))
                    {
                        string message = formatter(state, exception);
                        Provider._eventSource.FormattedMessage(
                            logLevel,
                            Provider.FactoryID,
                            Name,
                            eventId.ToString(),
                            message);
                    }

#if !NO_EVENTSOURCE_COMPLEX_TYPE_SUPPORT
                    // See if they want the message as its component parts.  
                    if (Provider._eventSource.IsEnabled(System.Diagnostics.Tracing.EventLevel.Critical, LoggingEventSource.Keywords.Message))
                    {
                        ExceptionInfo exceptionInfo = GetExceptionInfo(exception);
                        IEnumerable<KeyValuePair<string, string>> arguments = GetProperties(state);

                        Provider._eventSource.Message(
                            logLevel,
                            Provider.FactoryID,
                            Name,
                            eventId.ToString(),
                            exceptionInfo,
                            arguments);
                    }
#endif
                    // See if they want the json message
                    if (Provider._eventSource.IsEnabled(System.Diagnostics.Tracing.EventLevel.Critical, LoggingEventSource.Keywords.JsonMessage))
                    {
                        string exceptionJson = "{}";
                        if (exception != null)
                        {
                            ExceptionInfo exceptionInfo = GetExceptionInfo(exception);
                            var exceptionInfoData = new KeyValuePair<string, string>[] {
                                new KeyValuePair<string, string>("TypeName", exceptionInfo.TypeName),
                                new KeyValuePair<string, string>("Message", exceptionInfo.Message),
                                new KeyValuePair<string, string>("HResult", exceptionInfo.HResult.ToString()),
                                new KeyValuePair<string, string>("VerboseMessage", exceptionInfo.VerboseMessage),
                            };
                            exceptionJson = ToJson(exceptionInfoData);
                        }
                        IEnumerable<KeyValuePair<string, string>> arguments = GetProperties(state);
                        Provider._eventSource.MessageJson(
                            logLevel,
                            Provider.FactoryID,
                            Name,
                            eventId.ToString(),
                            exceptionJson,
                            ToJson(arguments));
                    }
                }

                public IDisposable BeginScope<TState>(TState state)
                {
                    var id = Interlocked.Increment(ref s_activityIds);

                    if (Provider._eventSource.IsEnabled(System.Diagnostics.Tracing.EventLevel.Critical, LoggingEventSource.Keywords.Message) ||
                        !Provider._eventSource.IsEnabled(System.Diagnostics.Tracing.EventLevel.Critical, LoggingEventSource.Keywords.JsonMessage))
                    {
#if !NO_EVENTSOURCE_COMPLEX_TYPE_SUPPORT
                        IEnumerable<KeyValuePair<string, string>> arguments = GetProperties(state);
                        Provider._eventSource.ActivityStart(id, Provider.FactoryID, Name, arguments);
                        return new ActivityScope(this, id, false);
#endif
                    }
                    else
                    {
                        IEnumerable<KeyValuePair<string, string>> arguments = GetProperties(state);
                        Provider._eventSource.ActivityJsonStart(id, Provider.FactoryID, Name, ToJson(arguments));
                        return new ActivityScope(this, id, true);
                    }
                }

                #region private 

                /// <summary>
                /// ActivityScope is just a IDisposable that knows how to send the ActivityStop event when it is 
                /// desposed.  It is part of the BeginScope() support.  
                /// </summary>
                private class ActivityScope : IDisposable
                {
                    public ActivityScope(EventSourceLogger logger, int id, bool isJsonStop)
                    {
                        _logger = logger;
                        _id = id;
                        _isJsonStop = isJsonStop;
                    }

                    public void Dispose()
                    {
                        if (_isJsonStop)
                            _logger.Provider._eventSource.ActivityJsonStop(_id, _logger.Provider.FactoryID, _logger.Name);
                        else 
                            _logger.Provider._eventSource.ActivityStop(_id, _logger.Provider.FactoryID, _logger.Name);
                    }   

                    EventSourceLogger _logger;
                    int _id;
                    bool _isJsonStop;
                }

                /// <summary>
                /// 'serializes' a given exception into an ExceptionInfo (that EventSource knows how to serialize)
                /// </summary>
                /// <param name="exception"></param>
                /// <returns></returns>
                private ExceptionInfo GetExceptionInfo(Exception exception)
                {
                    var exceptionInfo = new ExceptionInfo();
                    if (exception != null)
                    {
                        exceptionInfo.TypeName = exception.GetType().FullName;
                        exceptionInfo.Message = exception.Message;
                        exceptionInfo.HResult = exception.HResult;
                        exceptionInfo.VerboseMessage = exception.ToString();
                    }
                    return exceptionInfo;
                }

                /// <summary>
                /// Converts an ILogger state object into a set of key-value pairs (That can be send to a EventSource) 
                /// </summary>
                private IEnumerable<KeyValuePair<string, string>> GetProperties(object state)
                {
                    var arguments = new List<KeyValuePair<string, string>>();
                    var asKeyValues = state as IEnumerable<KeyValuePair<string, object>>;
                    if (asKeyValues != null)
                    {
                        foreach (var keyValue in asKeyValues)
                            arguments.Add(new KeyValuePair<string, string>(keyValue.Key, keyValue.Value.ToString()));
                    }
                    return arguments;
                }

                private static int s_activityIds;
                #endregion
            }

            LogLevel _defaultLevel;             // The default level for all loggers 
            string _filterSpec;                 // My filter specification. 
            EventSourceLogger _loggers;         // linked list of loggers I have created. 
            LoggingEventSource _eventSource;    // The eventSource I log to.  
            #endregion
        }

#if !NO_EVENTSOURCE_COMPLEX_TYPE_SUPPORT
        private LoggingEventSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat) { }
#endif

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            lock (this)
            {
                if ((command.Command == EventCommand.Update || command.Command == EventCommand.Enable))
                {
                    string filterSpec;
                    if (!command.Arguments.TryGetValue("FilterSpecs", out filterSpec))
                        filterSpec = "";        // This means turn on everything.  

                    SetFilterSpec(filterSpec);
                }
                else if (command.Command == EventCommand.Update || command.Command == EventCommand.Disable)
                {
                    SetFilterSpec(null);        // This means disable everything.  
                }
            }
        }

        /// <summary>
        /// Set the filtering specifcation.  null means turn off all loggers.   Empty string is turn on all providers.  
        /// </summary>
        /// <param name="filterSpec"></param>
        private void SetFilterSpec(string filterSpec)
        {
            _filterSpec = filterSpec;
            for (var cur = _loggingProviders; cur != null; cur = cur.Next)
                cur.SetFilterSpec(filterSpec);
        }

        string _filterSpec;
        EventSourceLoggerProvider _loggingProviders;
        #endregion
    }
}