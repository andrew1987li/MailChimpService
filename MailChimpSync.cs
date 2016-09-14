
using Mandrill;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace MailChimpSyncService
{
    public partial class MailChimpSync : ServiceBase
    {
        const string EVENT_SRC = "MailChimpTwilioSyncService";
        const string EVENT_LOG = "MailChimpTwilioSync_Default";
        
        private MandrillApi mandrill;
        private Twilio.TwilioRestClient twilio;
        private string FromEmail;
        private string FromName;
        private string Subject;
        private int Frequency;
        public int LimitPerGroup;
        private string MandrilApiKey;
        private string DatabaseTable;
        //twilioAccount, twilioAuthToken, twilioAccountResourceSid, twilioApiVersion, twilioBaseUrl);
        private string twilioAccountSid;
        private string twilioAuthToken;
        private string twilioAccountResourceSid;
        private string twilioApiVersion;
        private string twilioBaseUrl;
        private string twilioMessageFormat;
        private string twilioSetFinancialPhoneNumber;

        private class ConnectionSettings
        {
            public string Host = "localhost";
            public string User = "sa";
            public string Password = "S3tF1n@nZ";
            public string Database = "master";

            public void LoadFromAppSettings()
            {
                try
                {
                    var appSettings = ConfigurationManager.AppSettings;
                    Host = appSettings["server"];
                    User = appSettings["user"];
                    Password = appSettings["password"];
                    Database = appSettings["database"];
                }
                catch (ConfigurationErrorsException e)
                {
                    
                }
            }

            public string ConnectionString {  get
                {
                    return String.Format(
                        "user id={0};password={1};server={2};Trusted_Connection=yes;database={3};connection timeout=30", 
                        User, Password, Host, Database);
                    
                }
            }
        }

        private class CustomerInfo
        {
            public string FirstName;
            public string CellPhone;
            public string LoanNumber;
            public bool SendSuccessful;
            public override string ToString()
            {
                return "Customer: " + FirstName + "<" + CellPhone + ">";
            }
        }
        
        private class SendGroup
        {
            public string field;
            public int from;
            public int to;
        }

        System.Timers.Timer timer;
        List<SendGroup> sendGroups;

        public MailChimpSync()
        {
            InitializeComponent();
        }

        internal void TestStartupAndStop(string[] args)
        {
            this.OnStart(args);
            Console.ReadLine();
            this.OnStop();
        }

        protected override void OnStart(string[] args)
        {
           
            sendGroups = new List<SendGroup>
            {
                new SendGroup
                {
                    field = "Pot30",
                    from = 1,
                    to = 29
                },
                new SendGroup
                {
                    field = "Pot60",
                    from = 30,
                    to = 59
                },
                new SendGroup
                {
                    field = "Pot90",
                    from = 60,
                    to = 89
                },
                new SendGroup
                {
                    field = "Pot120",
                    from = 90,
                    to = 119
                },
                new SendGroup
                {
                    field = "ChargeOff",
                    from = 120,
                    to = 99999
                },
            };

            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                FromEmail = appSettings["fromEmail"];
                FromName = appSettings["fromName"];
                Frequency = Int32.Parse(appSettings["frequency"].ToString());
                LimitPerGroup = Int32.Parse(appSettings["limitPerGroup"].ToString());
                Subject = appSettings["subject"];
                MandrilApiKey = appSettings["mandrillApiKey"];
                DatabaseTable = appSettings["databaseTable"];
                MandrillTemplate = "";// appSettings["mandrillTemplate"];

                twilioAccountSid = appSettings["twilioAccountSid"];
                twilioAuthToken = appSettings["twilioAuthToken"];
                twilioAccountResourceSid = appSettings["twilioAccountResourceSid"];
                twilioApiVersion = appSettings["twilioApiVersion"];
                twilioBaseUrl = appSettings["twilioBaseUrl"];
                twilioMessageFormat = appSettings["twilioMessageFormat"];
                twilioSetFinancialPhoneNumber = appSettings["twilioSetFinancialPhoneNumber"];
            }

            catch (ConfigurationErrorsException e)
            {
                
            }

            mandrill = new MandrillApi(MandrilApiKey);
            twilio = new Twilio.TwilioRestClient(twilioAccountSid, twilioAuthToken);

            // Set up a timer to trigger every minute.
            timer = new System.Timers.Timer();
            timer.Interval = 60000 * Frequency; // 60 seconds
            timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
            timer.Start();
            OnTimer(null, null);   
        }

        string report = "";
        private string MandrillTemplate;

        private void WriteReport(string message, EventLogEntryType entryType = EventLogEntryType.Information)
        {
            var openTag = "";
            var closeTag = "";
            if (entryType == EventLogEntryType.Error)
            {
                openTag = "<span style='color:#AA1111'>";
                closeTag = "</span>";
            }
            report = report + "<p style='color:#444444; padding:0;margin:0 0 10px 10px;'>"+openTag + "<em>" + DateTime.Now.ToString("h:mm:ss tt") + "</em> "+ message + closeTag + "</p>";
        }

        async public void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
        {
            // Mon-Fri @ 9am-6pm EST.
            DateTime timeUtc = DateTime.UtcNow;
            TimeZoneInfo easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime easternTime = TimeZoneInfo.ConvertTimeFromUtc(timeUtc, easternZone);

            if(easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
            {
                return;
            }

            TimeSpan start = new TimeSpan(9, 0, 0); //9 o'clock
            TimeSpan end = new TimeSpan(18, 0, 0); //18 o'clock
            TimeSpan now = easternTime.TimeOfDay;

            if ((now < start) || (now > end))
            {
                //match foundre

                return;
            }


            var cs = new ConnectionSettings();
            cs.LoadFromAppSettings();

            report = "<h1 style='padding:0;margin:10px;color:#333333'>Twilio Email Report</h1>";

            SqlConnection conn = new SqlConnection(cs.ConnectionString);
            try {
                WriteReport("Starting...");
                conn.Open();
            } catch (Exception e)
            {
                WriteReport("Error with DB connection: " + e.ToString(), EventLogEntryType.Error);
                return;
            }


            foreach (var sendGroup in sendGroups)
            {
                WriteReport("Fetching from group: " + sendGroup.field);
                var customers = fetchLatest(conn, sendGroup).Take(LimitPerGroup);

                foreach (CustomerInfo customer in customers)
                {
                    WriteReport("Sent to customer: Loan #" + customer.LoanNumber );
                    await SendSms(conn, customer, sendGroup);
                }

            }

            try
            {
                conn.Close();
            }
            catch (Exception e)
            {
                WriteReport("Error Closing DB Connection: " + e.ToString(), EventLogEntryType.Error);
            }
            WriteReport("Complete");

            SendReportEmail();

        }

        protected override void OnStop()
        {
            //timer.Stop();
        }

        protected override void OnContinue()
        {
            base.OnContinue();
            //timer.Start();

        }


        private List<CustomerInfo> fetchLatest(SqlConnection conn, SendGroup sendGroup)
        {
            //ListResult lists = mailChimp.GetLists();
            var list = new List<CustomerInfo>();

            try
            {
                SqlDataReader reader = null;
                var sql = "select * from dbo." + DatabaseTable + " where DaysLate between @from and @to and " + sendGroup.field + " is null and CellPhone is not null and CellPhone != ''";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@from", sendGroup.from));
                cmd.Parameters.Add(new SqlParameter("@to", sendGroup.to));

                reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var customer = new CustomerInfo
                    {
                        FirstName = reader["Borrower"].ToString(),
                        CellPhone = reader["CellPhone"].ToString(),
                        LoanNumber = reader["LoanNumber"].ToString()
                    };
                    list.Add(customer);
                }
                reader.Close();
            }
            catch (Exception e)
            {
                WriteReport(e.ToString(), EventLogEntryType.Error);
            }

            return list;
        }

        async private Task SendSms(SqlConnection conn, CustomerInfo customer, SendGroup sendGroup)
        {
            /*twilio.SendMessage(
                twilioSetFinancialPhoneNumber,//SET Financial Number - must be an SMS-enabled Twilio number
                customer.CellPhone, // To number, if using Sandbox see note above 
            // message content
            string.Format(twilioMessageFormat, customer.FirstName, customer.LoanNumber)
            );
            */
            twilio.SendMessageWithService("MGf9553c29a398e34a09bbef6b3a3fad6e",
                customer.CellPhone,
                string.Format(twilioMessageFormat, customer.FirstName, customer.LoanNumber)
                );


            var r = MarkRemotely(conn, customer, sendGroup);
            if (r == 0)
            {
                WriteReport("Failed to updated DB for customer with Loan #" + customer.LoanNumber, EventLogEntryType.Error);
            }
        }

        //async private Task Send(SqlConnection conn, CustomerInfo customer, SendGroup sendGroup)
        //{
        //    var templateContent = new List<Mandrill.Models.TemplateContent>();
        //    templateContent.Add(new Mandrill.Models.TemplateContent()
        //    {
        //        Name = "loanNumber",
        //        Content = customer.LoanNumber
        //    });
        //    templateContent.Add(new Mandrill.Models.TemplateContent()
        //    {
        //        Name = "firstName",
        //        Content = customer.FirstName
        //    });

        //    var mndEmail = new List<Mandrill.Models.EmailAddress>();
        //    mndEmail.Add(new Mandrill.Models.EmailAddress(customer.Email, customer.FirstName));

        //    var message = new Mandrill.Models.EmailMessage()
        //    {
        //        To = mndEmail,
        //        FromName = FromName,
        //        FromEmail = FromEmail,
        //        Subject = Subject
        //    };
        //    var msqRequest = new Mandrill.Requests.Messages.SendMessageTemplateRequest(message, MandrillTemplate, templateContent);
            
        //   var results = await mandrill.SendMessageTemplate(msqRequest);

        //   foreach (var result in results)
        //    {
        //        customer.SendSuccessful = result.Status.ToString() == "sent" || result.Status.ToString() == "Sent";
        //        if (customer.SendSuccessful == false)
        //        {
        //            WriteReport("Unable to send email to customer with Loan #" + customer.LoanNumber + ", reason:" + result.RejectReason);
        //        }
        //        //customer.SendSuccessful = true;
        //    }

        //   if (customer.SendSuccessful)
        //    {
        //        var r = MarkRemotely(conn, customer, sendGroup);
        //        if (r == 0)
        //        {
        //            WriteReport("Failed to updated DB for customer with Loan #" + customer.LoanNumber, EventLogEntryType.Error);
        //        }
        //    }
            
        //}
        
        private int MarkRemotely(SqlConnection conn, CustomerInfo customer, SendGroup sendGroup)
        {
            var sql = "update dbo." + DatabaseTable + " set " + sendGroup.field + " = GETDATE() where LoanNumber = '" + customer.LoanNumber + "'";
            SqlCommand cmd = new SqlCommand(sql, conn);
            return cmd.ExecuteNonQuery();

        }

        async private void SendReportEmail()
        {
            string reportToEmail = "";

            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                reportToEmail = appSettings["reportToEmail"];            }
            catch (ConfigurationErrorsException e)
            {
                return;
            }

            var mndEmail = new List<Mandrill.Models.EmailAddress>();
            mndEmail.Add(new Mandrill.Models.EmailAddress(reportToEmail));

            var message = new Mandrill.Models.EmailMessage()
            {
                To = mndEmail,
                FromEmail = FromEmail,
                FromName = FromName,
                Subject = "Daily Email Notifications Report",
                Html = report
            };
            var msgRequest = new Mandrill.Requests.Messages.SendMessageRequest(message);
            //var msgRequest = new Mandrill.Requests.Messages.SendRawMessageRequest(report);

            await mandrill.SendMessage(msgRequest);

            Console.Write(report);
            
        }

    }
}

/*
{
    "key": "example key",
    "message": {
        "html": "<p>Example HTML content</p>",
        "text": "Example text content",
        "subject": "example subject",
        "from_email": "message.from_email@example.com",
        "from_name": "Example Name",
        "to": [
            {
                "email": "recipient.email@example.com",
                "name": "Recipient Name",
                "type": "to"
            }
        ],
        "headers": {
            "Reply-To": "message.reply@example.com"
        },
        "important": false,
        "track_opens": null,
        "track_clicks": null,
        "auto_text": null,
        "auto_html": null,
        "inline_css": null,
        "url_strip_qs": null,
        "preserve_recipients": null,
        "view_content_link": null,
        "bcc_address": "message.bcc_address@example.com",
        "tracking_domain": null,
        "signing_domain": null,
        "return_path_domain": null,
        "merge": true,
        "merge_language": "mailchimp",
        "global_merge_vars": [
            {
                "name": "merge1",
                "content": "merge1 content"
            }
        ],
        "merge_vars": [
            {
                "rcpt": "recipient.email@example.com",
                "vars": [
                    {
                        "name": "merge2",
                        "content": "merge2 content"
                    }
                ]
            }
        ],
        "tags": [
            "password-resets"
        ],
        "subaccount": "customer-123",
        "google_analytics_domains": [
            "example.com"
        ],
        "google_analytics_campaign": "message.from_email@example.com",
        "metadata": {
            "website": "www.example.com"
        },
        "recipient_metadata": [
            {
                "rcpt": "recipient.email@example.com",
                "values": {
                    "user_id": 123456
                }
            }
        ],
        "attachments": [
            {
                "type": "text/plain",
                "name": "myfile.txt",
                "content": "ZXhhbXBsZSBmaWxl"
            }
        ],
        "images": [
            {
                "type": "image/png",
                "name": "IMAGECID",
                "content": "ZXhhbXBsZSBmaWxl"
            }
        ]
    },
    "async": false,
    "ip_pool": "Main Pool",
    "send_at": "example send_at"
}
*/

/*
USE [ezLoan7]
GO

Object:  Table [dbo].[reports_active_accounts]    Script Date: 01/19/2016 16:27:36 

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET ANSI_PADDING ON
GO

CREATE TABLE[dbo].[reports_active_accounts](
	[LoanNumber]
[varchar](25) NOT NULL,

[LoanCategory] [varchar](3) NULL,
	[Delinquency]
[varchar](25) NOT NULL,

[DaysLate] [int]
NULL,
	[CSRID]
[varchar](50) NULL,
	[BranchID]
[int]
NOT NULL,

    [FirstPaymentDue] [smalldatetime]
NULL,
	[NextDueDate]
[smalldatetime]
NULL,
	[InterestPaidThroughDate]
[smalldatetime]
NULL,
	[AgingNextDue]
[smalldatetime]
NULL,
	[CashToBorrower]
[money]
NOT NULL,

    [CashToRefi] [money]
NOT NULL,

    [AmountFinanced] [money]
NOT NULL,

    [NoteAmount] [money]
NOT NULL,

    [FinanceCharge] [money]
NOT NULL,

    [NumberOfPayments] [smallint]
NOT NULL,

    [InterestRate] [money]
NOT NULL,

    [APR] [money]
NOT NULL,

    [PaidThruDate] [smalldatetime]
NULL,
	[CurrentBalance]
[money]
NOT NULL,

    [CollateralCode] [char](2) NULL,
	[AccountStatus]
[varchar](25) NULL,
	[PastDueAmount]
[money]
NULL,
	[LastPaidDate]
[smalldatetime]
NULL,
	[LastPaymentAmount]
[money]
NOT NULL,

    [BKFlag] [smallint]
NOT NULL,

    [DateTimeAdded] [datetime]
NULL,
	[PandIPaymentAmount]
[money]
NOT NULL,

    [LastCalledDate] [smalldatetime]
NULL,
	[WhoCalledLast]
[varchar](50) NULL,
	[ChargeOffDate]
[smalldatetime]
NULL,
	[LegalFlag]
[smallint]
NOT NULL,

    [ChargeOffAmount] [money]
NOT NULL,

    [PriorMonthBalance] [money]
NOT NULL,

    [AutoDraftIsOn] [smallint]
NOT NULL,

    [NextAutoPaymentDue] [smalldatetime]
NULL,
	[UnderwriterID]
[bigint]
NOT NULL,

    [debtRatio] [real]
NOT NULL,

    [PurposeOfLoan] [varchar](50) NULL,
	[OutsideCollectionAgency]
[bigint]
NOT NULL,

    [UnearnedAmortizedInterestBalance] [money]
NOT NULL,

    [PaidOffAccount] [varchar](25) NULL,
	[BorrowerType]
[int]
NULL,
	[HowPaidOff]
[smallint]
NOT NULL,

    [Military] [int]
NULL,
	[followupdate]
[smalldatetime]
NULL,
	[FICO]
[smallint]
NULL,
	[TodaysDate]
[smalldatetime]
NULL,
	[Borrower]
[varchar](50) NULL,
	[Email]
[varchar](50) NULL,
	[Pot30]
[smalldatetime]
NULL,
	[Pot60]
[smalldatetime]
NULL,
	[Pot90]
[smalldatetime]
NULL,
	[Pot120]
[smalldatetime]
NULL,
	[ChargeOff]
[smalldatetime]
NULL
) ON[PRIMARY]

GO

SET ANSI_PADDING ON
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_TypeOfLoan]  DEFAULT((0)) FOR[Delinquency]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_CSRID]  DEFAULT((0)) FOR[CSRID]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_BranchID]  DEFAULT((0)) FOR[BranchID]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_CashToBorrower]  DEFAULT((0)) FOR[CashToBorrower]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_CashToRefi]  DEFAULT((0)) FOR[CashToRefi]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_AmountFinanced]  DEFAULT((0)) FOR[AmountFinanced]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_NoteAmount]  DEFAULT((0)) FOR[NoteAmount]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_FinanceCharge]  DEFAULT((0)) FOR[FinanceCharge]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_NumberOfPayments]  DEFAULT((0)) FOR[NumberOfPayments]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_InterestRate]  DEFAULT((0)) FOR[InterestRate]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_APR]  DEFAULT((0)) FOR[APR]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_CurrentBalance]  DEFAULT((0)) FOR[CurrentBalance]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_LastPaymentAmount]  DEFAULT((0)) FOR[LastPaymentAmount]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_BKFlag]  DEFAULT((0)) FOR[BKFlag]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_PandIPaymentAmount]  DEFAULT((0)) FOR[PandIPaymentAmount]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_LegalFlag]  DEFAULT((0)) FOR[LegalFlag]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_ChargeOffAmount]  DEFAULT((0)) FOR[ChargeOffAmount]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_PriorMonthBalance]  DEFAULT((0)) FOR[PriorMonthBalance]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_AutoDraftIsOn]  DEFAULT((0)) FOR[AutoDraftIsOn]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_UnderwriterID]  DEFAULT((0)) FOR[UnderwriterID]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_debtRatio]  DEFAULT((0)) FOR[debtRatio]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_OutsideCollectionAgency]  DEFAULT((0)) FOR[OutsideCollectionAgency]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_UnearnedAmortizedInterestBalance]  DEFAULT((0)) FOR[UnearnedAmortizedInterestBalance]
GO

ALTER TABLE[dbo].[reports_active_accounts]
ADD CONSTRAINT[DF_reports_active_accounts_HowPaidOff]  DEFAULT((0)) FOR[HowPaidOff]
GO


*/
