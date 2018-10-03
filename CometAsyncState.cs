using System;
using System.Threading;
using System.Web;
using System.Collections.Generic;
using System.Web.UI;
using System.IO;
using System.Text;

namespace Kesco.Lib.Web.Comet
{
    /// <summary>
    ///     Класс-наследник от IAsyncResult для хранения параметров клиента
    /// </summary>
    public class CometAsyncState : IAsyncResult
    {
        /// <summary>
        ///     Асинхронный колбек
        /// </summary>
        public AsyncCallback AsyncCallback;

        /// <summary>
        ///     Контекст, где хранить все это и был создан класс-наследник от IAsyncResult
        /// </summary>
        public HttpContext CurrentContext;

        /// <summary>
        ///     Данные
        /// </summary>
        public object ExtraData;

        /// <summary>
        /// Максимальное количество предпринимаемых попыток отправки сообщений
        /// </summary>
        public const int MaxTries = 3;

        /// <summary>
        /// Количество оставшихся попыток отправки сообщений
        /// </summary>
        public int Tries;

        /// <summary>
        /// Очередь сообщений для отправки сообщений клиенту
        /// </summary>
        public Queue<CometMessage> Messages;

        public Page Page;

        /*
        private static void AddMessage(FileStream fs, CometMessage m, string g)
        {
            lock (fs)
            {
                byte[] msg = new UTF8Encoding(true).GetBytes(Environment.NewLine + m.Serialize() + g + Environment.NewLine);
                fs.Write(msg, 0, msg.Length);
            }
        }
        */

        // Конструктор
        public CometAsyncState(HttpContext context, AsyncCallback callback, object data)
        {
            CurrentContext = context;
            AsyncCallback = callback;
            ExtraData = data;
            IsCompleted = false;
            IsEditable = true;
            Start = DateTime.Now;

            Tries = MaxTries;
        }

        /// <summary>
        ///     Идентификатор
        /// </summary>
        public string ClientGuid { get; set; }

        // Завершим запрос
        public void CompleteRequest()
        {
            // При завершении запроса просто выставим флаг что он завершен
            // и вызовем callback
            IsCompleted = true;
            if (AsyncCallback != null)
            {
                try
                {
                    AsyncCallback(this);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Добавить сообщение в очередь
        /// </summary>
        /// <param name="m"></param>
        public int AddMessage(CometMessage m)
        {
            //Клиент уже разрегистрирован
            if (Tries < 1) return -1;

            if (null == Messages)
                Messages = new Queue<CometMessage>();

            if (Messages.Count < 1)
            {
                Tries = CometAsyncState.MaxTries;
            }

            //Если в очереди уже есть сообщение для обновления списка пользователей, то имеющееся сообщение можно просто обновить

            if (m.isUserList())
            {
                Queue<CometMessage>.Enumerator en = Messages.GetEnumerator();
                while (en.MoveNext())
                {
                    if (en.Current.isUserList())
                    {
                        //lock (CometServer.fs)
                        //{
                        //    byte[] g = new UTF8Encoding(true).GetBytes(Environment.NewLine + "Skip Message" + m.Serialize());
                        //    CometServer.fs.Write(g, 0, g.Length);
                        //}

                        en.Current.Message = m.Message;
                        return Messages.Count;
                    }
                }
            }

            //AddMessage(CometServer.fs, m, ClientGuid);

            Messages.Enqueue(m);
            return Messages.Count;
        }

        /// <summary>
        /// Отправка первого сообщения из очереди сообщений
        /// </summary>
        /// <param name="fTest">Если True, то метод был вызван после окончания ожидания, в случае неудачи следует уменьшить количество оставшихся попыток</param>
        public void SendMessage(bool fTest)
        {
            if (Messages == null) return;
            if (Messages.Count < 1) return;

            bool fFail = true;
            if (CurrentContext != null && CurrentContext.Session != null)
            {
                CometMessage message = Messages.Peek();

                try
                {
                    // пишем в выходной поток текущее сообщение
                    CurrentContext.Response.Write(message.IsV4Script
                          ? message.Message
                          : message.Serialize());
                    
                    Messages.Dequeue();
                    fFail = false;

                    //Сбрасываем счетчик неудачных попыток после каждой успешной отправки сообщения, если клиент не должен быть удален (например после Unregister)
                    if (Tries > 0) Tries = MaxTries;
                }
                catch
                {
                }
            }

            // После чего завершаем запрос - вот именно после этого результаты
            // запроса пойдут ко всем подключенным клиентам
            CompleteRequest();
            CurrentContext = null;

            if (fFail && fTest && --Tries < 1)
            {
                //Клиент будет отсоединен
                Messages = null;
            }
        }

        #region IAsyncResult Members

        /// <summary>
        ///     Заглушка для интерфейса IAsyncResult
        ///     Возвращает значение, показывающее, синхронно ли закончилась асинхронная операция.
        /// </summary>
        public Boolean CompletedSynchronously
        {
            get { return false; }
        }

        /// <summary>
        ///     Признак завершения запроса
        /// </summary>
        public bool IsCompleted { get; set; }


        /// <summary>
        ///     Объект редактируется или просматривается
        /// </summary>
        public bool IsEditable { get; set; }

        /// <summary>
        ///     Время последнего обновления Long-Polling соединения
        /// </summary>
        public DateTime Start { get; set; }

        /// <summary>
        /// Идентификатор сущности
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Название сущности
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     Возвращает определенный пользователем объект, который определяет или содержит в себе сведения об асинхронной
        ///     операции.
        /// </summary>
        public object AsyncState
        {
            get { return ExtraData; }
        }

        /// <summary>
        ///     Возвращает дескриптор WaitHandle, используемый для ожидания завершения асинхронной операции.
        /// </summary>
        public WaitHandle AsyncWaitHandle
        {
            get { return new ManualResetEvent(false); }
        }

        #endregion
    }
}