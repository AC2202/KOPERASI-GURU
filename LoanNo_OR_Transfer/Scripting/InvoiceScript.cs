using System;

namespace Scripting
{
    internal class InvoiceScript
    {
        public void AfterSave(AutoCount.Invoicing.Sales.Invoice.InvoiceEventArgs e)
        {
            if (e == null) return;
            if (e.MasterRecord == null) return;

            // 1) Sales Invoice DocNo
            string invDocNo = e.MasterRecord.DocNo == null ? "" : e.MasterRecord.DocNo.ToString();
            if (invDocNo == "") return;

            // 2) Read loan no from IV
            object loanObj = e.DBSetting.ExecuteScalar(
                "SELECT UDF_LOANNO FROM IV WHERE DocNo = ?", invDocNo);

            if (loanObj == null || loanObj == DBNull.Value) return;
            string loanNo = loanObj.ToString();

            // 3) Update ARInvoice loan no
            e.DBSetting.ExecuteNonQuery(@"
        UPDATE ARInvoice
        SET UDF_LOANNO = ?
        WHERE DocNo = ?
    ", loanNo, invDocNo);

            // 4) If cash book exists (already paid), update CB loan no too
            /*object cbCountObj = e.DBSetting.ExecuteScalar(@"
        SELECT COUNT(1)
        FROM CB cb
        JOIN ARPayment p ON p.DocKey = cb.SourceKey
        JOIN ARPaymentKnockOff k ON k.DocKey = p.DocKey
        JOIN ARInvoice ar ON ar.DocKey = k.KnockOffDocKey
        WHERE ar.DocNo = ?
    ", invDocNo);

            int cbCount = cbCountObj == null || cbCountObj == DBNull.Value ? 0 : Convert.ToInt32(cbCountObj);
            if (cbCount <= 0) return;

            e.DBSetting.ExecuteNonQuery(@"
        UPDATE cb
        SET cb.UDF_loanNo = ?
        FROM CB cb
        JOIN ARPayment p ON p.DocKey = cb.SourceKey
        JOIN ARPaymentKnockOff k ON k.DocKey = p.DocKey
        JOIN ARInvoice ar ON ar.DocKey = k.KnockOffDocKey
        WHERE ar.DocNo = ?
    ", loanNo, invDocNo);*/

            // ===== New Addition: Pass Member + StartDate + EndDate to ARInvoice =====

            // UDF keys (usually without "UDF_")
            string member = Convert.ToString(e.MasterRecord.UDF["MEMBER"] ?? "").Trim();

            // Use Converter to avoid crash if empty
            DateTime startDate = AutoCount.Converter.ToDateTime(e.MasterRecord.UDF["STARTDATE"]);
            DateTime endDate = AutoCount.Converter.ToDateTime(e.MasterRecord.UDF["ENDDATE"]);

            // If your UDF could be empty, Converter will return 0001-01-01.
            // Decide whether to update only when valid date:
            bool hasStart = startDate.Year > 1900;
            bool hasEnd = endDate.Year > 1900;

            // Update ARInvoice (only update date if valid; member update if not empty)
            e.DBSetting.ExecuteNonQuery(@"
    UPDATE ARInvoice
    SET 
        UDF_MEMBER    = ?,
        UDF_STARTDATE = ?,
        UDF_ENDDATE   = ?
    WHERE DocNo = ?
",
            member,
            hasStart ? (object)startDate : DBNull.Value,
            hasEnd ? (object)endDate : DBNull.Value,
            invDocNo
            );



        }




    }
}