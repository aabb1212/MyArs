using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Xunit;
using MimeKit;
using Ars.Commom.Tool.Extension;

namespace ImapIdle
{
    public class Program
    {
        // Connection-related properties
        public const SecureSocketOptions SslOptions = SecureSocketOptions.Auto;
        public const string Host = "imap.qq.com";
        public const int Port = 993;

        // Authentication-related properties
        public const string Username = "1432507436@qq.com";
        public const string Password = "xmyezgingmucbacd";

        [Fact]
        public async Task Testxx()
        {
            using (var client = new IdleClient())
            {
                Console.WriteLine("Hit any key to end the demo.");

                await client.RunAsync();

                client.Exit();
            }
        }
    }

    class IdleClient : IDisposable
    {
        List<(string, IMessageSummary)> messages;
        CancellationTokenSource cancel;
        CancellationTokenSource done;
        bool messagesArrived;
        ImapClient client;

        public IdleClient()
        {
            client = new ImapClient(new ProtocolLogger(Console.OpenStandardError()));
            messages = new List<(string, IMessageSummary)>();
            cancel = new CancellationTokenSource();
        }

        async Task ReconnectAsync()
        {
            if (!client.IsConnected)
                await client.ConnectAsync(Program.Host, Program.Port, Program.SslOptions, cancel.Token);

            if (!client.IsAuthenticated)
            {
                await client.AuthenticateAsync(Program.Username, Program.Password, cancel.Token);

                await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancel.Token);
            }
        }

        async Task FetchMessageSummariesAsync(bool print)
        {
            IList<IMessageSummary> fetched;

            do
            {
                try
                {
                    // fetch summary information for messages that we don't already have
                    int startIndex = messages.Count;

                    if (print)
                        fetched = await client.Inbox.FetchAsync(startIndex, -1, MessageSummaryItems.Full | MessageSummaryItems.UniqueId | MessageSummaryItems.EmailId | MessageSummaryItems.Headers, cancel.Token);
                    else
                        fetched = await client.Inbox.FetchAsync(startIndex, -1, MessageSummaryItems.Fast | MessageSummaryItems.UniqueId, cancel.Token);

                    break;
                }
                catch (ImapProtocolException)
                {
                    // protocol exceptions often result in the client getting disconnected
                    await ReconnectAsync();
                }
                catch (IOException)
                {
                    // I/O exceptions always result in the client getting disconnected
                    await ReconnectAsync();
                }
            } while (true);

            foreach (var message in fetched)
            {
                MimeEntity text = null;
                MimeEntity html = null;
                if (message.TextBody != null)
                {
                    // this will download *just* the text/plain part
                    text = client.Inbox.GetBodyPart(message.UniqueId, message.TextBody);
                }
                else if (message.HtmlBody != null)
                {
                    // this will download *just* the text/html part
                    html = client.Inbox.GetBodyPart(message.UniqueId, message.HtmlBody);
                }

                //判断回复id，持久化邮件内容到数据库
                if (print) 
                {

                }
                string key = null == text
                    ? null == html 
                        ? string.Empty 
                        : ((TextPart)html).Text.ToString().Unescape()
                    : ((MimeKit.TextPart)text).Text.ToString().Unescape();
                messages.Add((key, message));
            }
        }

        async Task WaitForNewMessagesAsync()
        {
            do
            {
                try
                {
                    if (client.Capabilities.HasFlag(ImapCapabilities.Idle))
                    {
                        // Note: IMAP servers are only supposed to drop the connection after 30 minutes, so normally
                        // we'd IDLE for a max of, say, ~29 minutes... but GMail seems to drop idle connections after
                        // about 10 minutes, so we'll only idle for 9 minutes.
                        using (done = new CancellationTokenSource(new TimeSpan(0, 9, 0)))
                        {
                            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token, done.Token))
                            {
                                await client.IdleAsync(linked.Token);

                                // throw OperationCanceledException if the cancel token has been canceled.
                                cancel.Token.ThrowIfCancellationRequested();
                            }
                        }
                    }
                    else
                    {
                        // Note: we don't want to spam the IMAP server with NOOP commands, so lets wait a minute
                        // between each NOOP command.
                        await Task.Delay(new TimeSpan(0, 1, 0), cancel.Token);
                        await client.NoOpAsync(cancel.Token);
                    }
                    break;
                }
                catch (ImapProtocolException)
                {
                    // protocol exceptions often result in the client getting disconnected
                    await ReconnectAsync();
                }
                catch (IOException)
                {
                    // I/O exceptions always result in the client getting disconnected
                    await ReconnectAsync();
                }
            } while (true);
        }

        async Task IdleAsync()
        {
            do
            {
                try
                {
                    await WaitForNewMessagesAsync();

                    if (messagesArrived)
                    {
                        await FetchMessageSummariesAsync(true);
                        messagesArrived = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            } while (!cancel.IsCancellationRequested);
        }

        public async Task RunAsync()
        {
            // connect to the IMAP server and get our initial list of messages
            try
            {
                await ReconnectAsync();
                await FetchMessageSummariesAsync(false);
            }
            catch (OperationCanceledException)
            {
                await client.DisconnectAsync(true);
                return;
            }

            // keep track of changes to the number of messages in the folder (this is how we'll tell if new messages have arrived).
            client.Inbox.CountChanged += OnCountChanged;

            // keep track of messages being expunged so that when the CountChanged event fires, we can tell if it's
            // because new messages have arrived vs messages being removed (or some combination of the two).
            client.Inbox.MessageExpunged += OnMessageExpunged;

            // keep track of flag changes
            client.Inbox.MessageFlagsChanged += OnMessageFlagsChanged;

            await IdleAsync();

            client.Inbox.MessageFlagsChanged -= OnMessageFlagsChanged;
            client.Inbox.MessageExpunged -= OnMessageExpunged;
            client.Inbox.CountChanged -= OnCountChanged;

            await client.DisconnectAsync(true);
        }

        // Note: the CountChanged event will fire when new messages arrive in the folder and/or when messages are expunged.
        void OnCountChanged(object sender, EventArgs e)
        {
            var folder = (ImapFolder)sender;

            // Note: because we are keeping track of the MessageExpunged event and updating our
            // 'messages' list, we know that if we get a CountChanged event and folder.Count is
            // larger than messages.Count, then it means that new messages have arrived.
            if (folder.Count > messages.Count)
            {
                int arrived = folder.Count - messages.Count;

                if (arrived > 0)
                {
                    Console.WriteLine("\t{0} new messages have arrived.", arrived);
                }

                // Note: your first instict may be to fetch these new messages now, but you cannot do
                // that in this event handler (the ImapFolder is not re-entrant).
                //
                // Instead, cancel the `done` token and update our state so that we know new messages
                // have arrived. We'll fetch the summaries for these new messages later...
                messagesArrived = true;
                done?.Cancel();
            }
        }

        void OnMessageExpunged(object sender, MessageEventArgs e)
        {
            var folder = (ImapFolder)sender;

            if (e.Index < messages.Count)
            {
                var message = messages[e.Index];

                Console.WriteLine("{0}: message #{1} has been expunged: {2}", folder, e.Index, message.Item2.Envelope.Subject);

                // Note: If you are keeping a local cache of message information
                // (e.g. MessageSummary data) for the folder, then you'll need
                // to remove the message at e.Index.
                messages.RemoveAt(e.Index);
            }
            else
            {
                Console.WriteLine("{0}: message #{1} has been expunged.", folder, e.Index);
            }
        }

        void OnMessageFlagsChanged(object sender, MessageFlagsChangedEventArgs e)
        {
            var folder = (ImapFolder)sender;

            Console.WriteLine("{0}: flags have changed for message #{1} ({2}).", folder, e.Index, e.Flags);
        }

        public void Exit()
        {
            cancel.Cancel();
        }

        public void Dispose()
        {
            client.Dispose();
            cancel.Dispose();
            done?.Dispose();
        }
    }
}