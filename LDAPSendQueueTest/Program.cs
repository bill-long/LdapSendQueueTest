using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LDAPSendQueueTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string dcName = args[0];
            string container = args[1];
            string filter = args[2];
            int numThreads = int.Parse(args[3]);
            int pageSize = int.Parse(args[4]);
            var ldapConnection = new LdapConnection(dcName);
            ldapConnection.Timeout = TimeSpan.FromSeconds(120);
            ldapConnection.Bind();

            var threadList = new System.Collections.Generic.List<System.Threading.Thread>();
            var workerList = new List<QueryWorker>();
            for (int x = 0; x < numThreads; x++)
            {
                Console.WriteLine("Starting thread...");
                var worker = new QueryWorker(ldapConnection, container, filter, pageSize);
                workerList.Add(worker);
                var thread = new System.Threading.Thread(new System.Threading.ThreadStart(worker.Go));
                threadList.Add(thread);
                thread.Start();
            }

            Console.WriteLine("Waiting for threads to complete...");
            bool atLeastOneThreadActive = false;
            do
            {
                atLeastOneThreadActive = false;

                var searchRequest = new SearchRequest(container, "(objectClass=*)", SearchScope.Base, null);
                SearchResponse searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);

                foreach (var thread in threadList)
                {
                    if (thread.IsAlive)
                        atLeastOneThreadActive = true;
                }

            } while (atLeastOneThreadActive);

            foreach (var worker in workerList)
            {
                foreach (var outputString in worker.output)
                    Console.WriteLine(outputString);

                Console.WriteLine();
            }
        }
    }

    public class QueryWorker
    {
        LdapConnection ldapConn;
        string container;
        string filter;
        int pageSize;
        public List<string> output = new List<string>();
        public QueryWorker(LdapConnection ldapConn, string container, string filter, int pageSize)
        {
            this.ldapConn = ldapConn;
            this.container = container;
            this.filter = filter;
            this.pageSize = pageSize;
        }

        public void Go()
        {
            try
            {
                string filter = "(objectClass=*)";
                var searchRequest = new SearchRequest(container, filter, SearchScope.Subtree, null);
                var pageControl = new PageResultRequestControl(pageSize);
                searchRequest.Controls.Add(pageControl);

                int pageCount = 0;
                while (true)
                {
                    pageCount++;
                    SearchResponse searchResponse = (SearchResponse)ldapConn.SendRequest(searchRequest);
                    var pageResponse = (PageResultResponseControl)searchResponse.Controls[0];
                    output.Add(string.Format("Thread {0} retrieved page {1} containing {2} entries.",
                        System.Threading.Thread.CurrentThread.ManagedThreadId, pageCount, searchResponse.Entries.Count));

                    if (pageResponse.Cookie.Length == 0)
                        break;

                    pageControl.Cookie = pageResponse.Cookie;
                }

                output.Add(string.Format("Thread {0} completed.", System.Threading.Thread.CurrentThread.ManagedThreadId));
            }
            catch (Exception exc)
            {
                output.Add("Exception: " + exc.Message);
            }
        }
    }
}
