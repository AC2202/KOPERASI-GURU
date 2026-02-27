using AutoCount;
using AutoCount.Authentication;
using AutoCount.PlugIn;
using AutoCount.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoanNo_OR_Transfer
{
    public class PlugInit : AutoCount.PlugIn.BasePlugIn
    {
        public PlugInit() : base(new Guid("{FE9B01CC-C53D-4978-9110-3731CC0C2D70}"), "Automated Payment Status & Member Data Synchronization", "2.2.0", "engkeat.cheow@softwaredepot.com.my")
        {
            SetMinimumAccountingVersionRequired("2.2.22.30");
            SetIsFreeLicense(false);
            SetDevExpressComponentVersionRequired("22.2.7");
            SetCopyright("Software Depot Sdn Bhd");
        }

        public override bool BeforeLoad(BeforeLoadArgs e)
        {
            // ===== LICENSE VERIFICATION =====
            string allowName = "KOPERASI GURU PULAU PINANG BERHAD";
            string allowAddr1 = "1-G, KING STREET,";


            string companyName = "";
            string addr1 = "";

            if (e != null && e.CompanyProfile != null)
            {
                companyName = (e.CompanyProfile.CompanyName ?? "");
                addr1 = (e.CompanyProfile.Address1 ?? "");
            }

            companyName = companyName.Trim().ToUpper();
            addr1 = addr1.Trim().ToUpper();

            bool okName = companyName == (allowName ?? "").Trim().ToUpper();
            bool okAddr = addr1 == (allowAddr1 ?? "").Trim().ToUpper();

            if (!okName || !okAddr)
            {
                AutoCount.AppMessage.ShowMessage(
                    "This plugin is not licensed for this account book.\n\n");
                return false;
            }

            e.MainMenuCaption = "Software Depot Plugin";
            AutoCount.Scripting.ScriptManager scriptManager =
                AutoCount.Scripting.ScriptManager.GetOrCreate(e.DBSetting);
            scriptManager.RegisterByType("IV", typeof(Scripting.InvoiceScript));            
            scriptManager.RegisterByType("ARPayment", typeof(Scripting.ARPaymentScript));
            return base.BeforeLoad(e);
        }
        public override void GetLicenseStatus(LicenseStatusArgs e)
        {
            e.LicenseStatus = LicenseStatus.Custom;
            e.CustomLicenseStatus = $"License is registered to {e.CompanyProfile.CompanyName}";
        }
        [MenuItem("Plugin Info", 1)]
        public class StockTransferApprovalMenu
        {
            private readonly UserSession _session;
            public StockTransferApprovalMenu(UserSession session)
            {
                _session = session;

                AutoCount.AppMessage.ShowInformationMessage(
                "Customization Request Summary\n\n" +

                "Date: 24 Feb 2026\n\n"

                );

            }
        }
    }
}
