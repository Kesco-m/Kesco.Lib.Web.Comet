using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;

namespace Kesco.Lib.Web.Comet
{
    /// <summary>
    ///     Указатель на функцию, вызывается после обновления списка клиентов
    /// </summary>
    /// <param name="sender">Инициатор вызова</param>
    public delegate void NotifyClientsEventHandler(CometAsyncState state, string clientGuid = null, int status = 0);

    /// Указатель на функцию, вызывается после обновление списка клиентов
    /// </summary>
    /// <param name="sender">Инициатор вызова</param>
    public delegate void NotifySendMessageEventHandler(string id, string name, string clientGuid, string message);

    // Собственно, серверная часть Comet
    public static class CometServer
    {
        //Путь к файлу логирования comet-сервера
        public const string COMET_LOG_FILE = "C:\\scripts\\comet.txt";

        /// <summary>
        ///     Объект синхронизации записи в лог
        /// </summary>
        private static readonly object _lockCometLog = new object();

        /// <summary>
        ///     Поток обработки сообщений в очередях
        /// </summary>
        private static Thread _procesor;

        // вспомогательный объект для блокировки ресурсов многопоточного приложения
        private static readonly object _lock = new object();

        // Список, хранящий состояние всех подключенных клиентов
        private static readonly List<CometAsyncState> _clientStateList = new List<CometAsyncState>();

        /// <summary>
        ///     Объект синхронизации потоков
        /// </summary>
        public static object SyncLock
        {
            get { return _lock; }
        }

        /// <summary>
        ///     Подключенные клиенты
        /// </summary>
        public static List<CometAsyncState> Connections
        {
            get { return _clientStateList; }
        }

        /// <summary>
        ///     Функция записи в лог
        /// </summary>
        /// <param name="message"></param>
        public static void WriteLog(string message)
        {
            //включить логирование
            //lock (_lockCometLog)
            //{
            //    using (var sw = File.AppendText(COMET_LOG_FILE))
            //    {
            //        sw.WriteLine(DateTime.Now + " " + message);
            //    }
            //}
        }

        /// <summary>
        ///     Указатель на событие, отслеживающие необходимость уведомления клиентов
        /// </summary>
        public static event NotifyClientsEventHandler NotifyClients;

        /// <summary>
        ///     Событие, отслеживающие необходимость уведомления клиентов
        /// </summary>
        /// <param name="state">Объект-подключения</param>
        /// <param name="clientGuid">Идентификатор страницы</param>
        /// <param name="status">Статус собфтия</param>
        private static void OnNotifyClients(CometAsyncState state, string clientGuid = null, int status = 0)
        {
            if (NotifyClients != null)
            {
                if (clientGuid == null && state == null ||
                    clientGuid == null && state != null && state.ClientGuid == null) return;

                NotifyClients(state, clientGuid, status);
            }
        }

        /// <summary>
        ///     Указатель на функцию обработки события получения сообщения
        /// </summary>
        public static event NotifySendMessageEventHandler NotifyMessages;

        /// <summary>
        ///     Событие, отслеживающее необходимость обработать сообщение
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="clientGuid"></param>
        /// <param name="message"></param>
        public static void OnNotifyMessage(string id, string name, string clientGuid, string message)
        {
            if (NotifyMessages != null)
                NotifyMessages(id, name, clientGuid, message);
        }
        // Возвращаем сообщение каждому подключенному клиенту
        /*
        public static void PushMessage(CometMessage pushMessage, string clientGuid = "")
        {
            lock (_lock)
            {
                // Пробегаем по списку всех подключенных клиентов
                foreach (var clientState in _clientStateList)
                {
                    if (clientState.CurrentContext.Session != null)
                    {
                        if (clientGuid != "" && clientState.ClientGuid != clientGuid) continue;
                        // И пишем в выходной поток текущее сообщение
                        clientState.CurrentContext.Response.Write(pushMessage.IsV4Script
                            ? pushMessage.Message
                            : pushMessage.Serialize());
                        // После чего завершаем запрос - вот именно после этого результаты 
                        // запроса пойдут ко всем подключенным клиентам
                        clientState.CompleteRequest();
                    }
                }
            }
        }*/

        /// <summary>
        ///     Отправка сообщения
        /// </summary>
        /// <param name="message">Сообщение</param>
        /// <param name="clientGuid">Идентификатор страницы</param>
        public static void PushMessage(CometMessage message, string clientGuid = "")
        {
            MsgProcessor.AddMessage(message, clientGuid);
        }

        /// <summary>
        ///     Отправка сообщения
        /// </summary>
        /// <param name="message">Сообщение</param>
        /// <param name="filter">Фильтр клиентов</param>
        public static void PushMessage(CometMessage message, Predicate<CometAsyncState> filter)
        {
            MsgProcessor.AddMessage(message, filter);
        }

        /// <summary>
        ///     Метод запуска сервера
        /// </summary>
        public static void Start()
        {
            _procesor = new Thread(MsgProcessor.Processing);
            _procesor.Start();
        }

        /// <summary>
        ///     Метод запуска сервера
        /// </summary>
        public static void Process()
        {
            MsgProcessor.TryNow();
        }

        /// <summary>
        ///     Метод остановки сервера
        /// </summary>
        public static void Stop()
        {
            lock (_lock)
            {
                WriteLog("Stop:->" + _clientStateList.Count);
                _clientStateList.ForEach(delegate(CometAsyncState cas)
                {
                    PushMessage(
                        new CometMessage
                        {
                            Message =
                                "Внимание! Соединение с приложением потеряно. Через 5 сек. соединение будет восстановлено и Вы сможете продолжить работу.",
                            UserName = "", Status = 2, ClientGuid = cas.ClientGuid, Reload = 1
                        }, cas.ClientGuid);
                    cas.CompleteRequest();
                    WriteLog("STOP APPLICATION -> " + cas.ClientGuid);
                });
                _clientStateList.Clear();
            }

            MsgProcessor.Stop();
            _procesor.Join();
        }

        /// <summary>
        ///     Срабатывает кажды раз при запуске клиентом очережного запроса Long poll
        ///     так как при этом HttpContext клиента изменяется, то надо обновить
        ///     все изменившиеся данные клиента в списке, идентифицируемом по гуиду,
        ///     который у клиента в течение работы остается постоянным
        /// </summary>
        /// <param name="state">Объект-подключение</param>
        /// <param name="clientGuid">Идентификатор страницы</param>
        /// <param name="firstConnect">В первый ли раз подключается клиент</param>
        public static void UpdateClient(CometAsyncState state, string clientGuid, bool firstConnect = false)
        {
            WriteLog("UpdateClient -> " + clientGuid);
            var existState = false;
            lock (_lock)
            {
                // ищем клиента в списке по его гуиду
                var clientState = _clientStateList.FirstOrDefault(s => s.ClientGuid == clientGuid);
                if (clientState != null)
                {
                    // и если он нашелся, то обновляем все его параметры
                    if (clientState.Start != DateTime.MinValue)
                        clientState.Start = DateTime.Now;

                    clientState.IsCompleted = false;
                    clientState.Id = state.Id;
                    clientState.Name = state.Name;
                    clientState.IsEditable = state.IsEditable;
                    clientState.CurrentContext = state.CurrentContext;
                    clientState.ExtraData = state.ExtraData;
                    clientState.AsyncCallback = state.AsyncCallback;

                    existState = true;
                }
            }

            if (!existState)
            {
                if (state != null)
                {
                    state.ClientGuid = clientGuid;
                    RegisterClient(state, false);
                    WriteLog("-> LOST CONNECTION  UnregisterClient by state -> " + clientGuid);
                    UnregisterClient(state, true);
                }
                else
                {
                    throw new Exception("Отсутствует объект state! Ошибка организации вызова клиента!");
                }
            }
        }

        /// <summary>
        ///     Очистка простроченных соединенй
        /// </summary>
        public static void ClearExpiredConnections()
        {
            WriteLog("Start ClearExpiredConnections, count=" + _clientStateList.Count);

            var listClientGuid = new List<string>();
            lock (_lock)
            {
                var expiredDate = DateTime.Now;
                for (var i = _clientStateList.Count - 1; i > -1; i--)
                {
                    var clientState = _clientStateList[i];
                    var workTime = expiredDate - clientState.Start;


                    WriteLog("Check " + clientState.ClientGuid + " time work ->" + workTime);

                    if (!(workTime.TotalMinutes > 15)) continue;


                    WriteLog(clientState.ClientGuid + " - unregister");

                    listClientGuid.Add(clientState.ClientGuid);

                    PushMessage(new CometMessage
                    {
                        Message = "unregister",
                        UserName = clientState.CurrentContext == null
                            ? ""
                            : clientState.CurrentContext.User.Identity.Name,
                        Status = 1,
                        ClientGuid = clientState.ClientGuid
                    });

                    _clientStateList.RemoveAt(i);
                }
            }

            foreach (var x in listClientGuid) OnNotifyClients(null, x, 1);
        }

        // Регистрация клиента
        public static void RegisterClient(CometAsyncState state, bool notifyClients = true)
        {
            lock (_lock)
            {
                // Присваиваем гуид и добавляем в список
                if (!_clientStateList.Exists(x => x.ClientGuid == state.ClientGuid))
                    _clientStateList.Add(state);

                WriteLog("EXISTS CONNECTIONS:");

                foreach (var clientState in Connections)
                    WriteLog("     ->" + clientState.ClientGuid + " " + clientState.Tries + " " +
                             clientState.Start.ToLongTimeString());
            }

            if (notifyClients)
                OnNotifyClients(state, state.ClientGuid);
        }

        // Разрегистрация клиента
        public static void UnregisterClient(CometAsyncState client, bool reload = false)
        {
            var m = new CometMessage
            {
                Message = "unregister",
                UserName = client.CurrentContext.User.Identity.Name,
                Status = 1,
                Reload = reload ? 1 : 0,
                ClientGuid = client.ClientGuid
            };

            lock (_lock)
            {
                //Очередь сообщений очищаем, клиент уже не хочет ничего принимать
                if (null != client.Messages)
                    client.Messages.Clear();

                client.AddMessage(m);

                // Клиент будет удален из списка после отправки последнего сообщения ему
                client.Tries = 0;
            }

            if (client.Id > 0)
                OnNotifyClients(client, null, 1);

            Process();
        }

        // Разрегистрация клиента
        public static void UnregisterClient(string clientGuid, bool serverCompeteRequest = true)
        {
            WriteLog("Start UnregisterClient by Guid -> " + clientGuid);

            var m = new CometMessage
            {
                Message = "unregister",
                UserName = HttpContext.Current == null ? "noname" : HttpContext.Current.User.Identity.Name,
                Status = 1,
                ClientGuid = clientGuid
            };

            lock (_lock)
            {
                // Клиент будет удален из списка после отправки последнего сообщения ему
                var client = _clientStateList.FirstOrDefault(x => x.ClientGuid == clientGuid);

                if (client != null)
                {
                    //Очередь сообщений очищаем, клиент уже не хочет ничего принимать
                    if (null != client.Messages)
                        client.Messages.Clear();

                    //если требуется закрыть соединение с сервера
                    if (serverCompeteRequest)
                        client.AddMessage(m);

                    // Клиент будет удален из списка после отправки последнего сообщения ему
                    client.Tries = 0;
                }
            }

            OnNotifyClients(null, clientGuid, 1);
            Process();
        }

        /// <summary>
        ///     Класс объекта обрабатывающего Long Pooling запросы
        /// </summary>
        private class MsgProcessor
        {
            private const int timeout = 60000; //1 минута
            private static readonly bool fStop = false;

            private static readonly AutoResetEvent msgEvent = new AutoResetEvent(false);
            private static readonly AutoResetEvent stopEvent = new AutoResetEvent(false);

            private static readonly WaitHandle[] waitHandles = {msgEvent, stopEvent};

            /// <summary>
            ///     Добавить сообщение в очередь для клиентов удовлетворяющих заданному условию
            /// </summary>
            /// <param name="m">Сообщение</param>
            /// <param name="filter">Фильтр для определения клиентов</param>
            public static void AddMessage(CometMessage m, Predicate<CometAsyncState> filter)
            {
                lock (SyncLock)
                {
                    foreach (var clientState in Connections)
                        if (filter(clientState))
                            clientState.AddMessage(m);
                }
            }

            /// <summary>
            ///     Добавить сообщение в очередь для клиентов с указанным guid
            /// </summary>
            /// <param name="m">Сообщение</param>
            /// <param name="clientGuid">IP- страницы</param>
            public static void AddMessage(CometMessage m, string clientGuid = "")
            {
                if (clientGuid == "")
                {
                    Predicate<CometAsyncState> f = client =>
                    {
                        return clientGuid == "" || client.ClientGuid == clientGuid;
                    };
                    AddMessage(m, f);
                }
                else
                {
                    var client = Connections.FirstOrDefault(c => c.ClientGuid == clientGuid);
                    if (client != null)
                        client.AddMessage(m);
                }
            }

            /// <summary>
            ///     Обработка подготовленных к отправке сообщений
            /// </summary>
            /// <param name="obj">Объект, заглукшка</param>
            public static void Processing(object obj)
            {
                while (!fStop)
                {
                    var result = WaitHandle.WaitAny(waitHandles, timeout);

                    if (result == 1)
                        break;

                    lock (SyncLock)
                    {
                        foreach (var clientState in Connections)
                        {
                            WriteLog("SEND MESSAGE");
                            clientState.SendMessage(WaitHandle.WaitTimeout == result);
                        }

                        var before = Connections.Count;
                        Connections.RemoveAll(
                            st => st.Tries < 1 && (null == st.Messages || st.Messages.Count < 1));

                        if (before != Connections.Count)
                        {
                            var sb = new StringBuilder();

                            foreach (var clientState in Connections)
                                sb.Append(" " + clientState.ClientGuid + " " + clientState.Tries + " " +
                                          clientState.Start.ToLongTimeString());
                        }
                    }
                }
            }

            /// <summary>
            ///     Инициализация выполения события
            /// </summary>
            public static void TryNow()
            {
                msgEvent.Set();
            }

            /// <summary>
            ///     Останвока события
            /// </summary>
            public static void Stop()
            {
                stopEvent.Set();
            }
        }
    }
}