using System;
using System.Linq;
using System.Threading;

namespace Scripting
{
    internal class ARPaymentScript
    {
        public void AfterSave(AutoCount.ARAP.ARPayment.ARPaymentEventArgs e)
        {
            if (e == null || e.MasterRecord == null) return;

            long payDocKey = AutoCount.Converter.ToInt64(e.MasterRecord.DocKey);
            if (payDocKey <= 0) return;

            // OR No = this ARPayment DocNo
            string orNo = Convert.ToString(
                e.DBSetting.ExecuteScalar("SELECT DocNo FROM ARPayment WHERE DocKey = ?", payDocKey)
            ).Trim();

            if (string.IsNullOrWhiteSpace(orNo)) return;

            // 1) Append ORNo into ARInvoice.UDF_ORNO (comma separated, no duplicate)
            e.DBSetting.ExecuteNonQuery(@"
UPDATE i
SET i.UDF_ORNO =
    CASE 
        WHEN i.UDF_ORNO IS NULL OR LTRIM(RTRIM(i.UDF_ORNO)) = '' THEN ?
        WHEN CHARINDEX(?, ',' + REPLACE(i.UDF_ORNO,' ','') + ',') > 0 THEN i.UDF_ORNO
        ELSE i.UDF_ORNO + ',' + ?
    END
FROM ARInvoice i
JOIN ARPaymentKnockOff k ON k.KnockOffDocKey = i.DocKey
WHERE k.DocKey = ?
", orNo, orNo, orNo, payDocKey);

            // 2) Update Paid flag for knocked invoices (Outstanding <= 0 => T else F)
            e.DBSetting.ExecuteNonQuery(@"
UPDATE i
SET i.UDF_PAID = CASE WHEN ISNULL(i.Outstanding,0) <= 0 THEN 'T' ELSE 'F' END
FROM ARInvoice i
JOIN ARPaymentKnockOff k ON k.KnockOffDocKey = i.DocKey
WHERE k.DocKey = ?
", payDocKey);

            // 3) Update Cash Book tick (ALL related OR for the affected invoices)
            e.DBSetting.ExecuteNonQuery(@"
;WITH Inv AS (
    SELECT DISTINCT k.KnockOffDocKey AS InvKey
    FROM ARPaymentKnockOff k
    WHERE k.DocKey = ?
),
Pay AS (
    SELECT DISTINCT k2.DocKey AS PayKey
    FROM ARPaymentKnockOff k2
    JOIN Inv ON Inv.InvKey = k2.KnockOffDocKey
)
UPDATE cb
SET cb.UDF_PAID =
    CASE 
        WHEN EXISTS(
            SELECT 1
            FROM ARPaymentKnockOff k3
            JOIN ARInvoice i3 ON i3.DocKey = k3.KnockOffDocKey
            WHERE k3.DocKey = cb.SourceKey
              AND ISNULL(i3.Outstanding,0) > 0
        ) THEN 'F'
        ELSE 'T'
    END
FROM CB cb
JOIN Pay ON Pay.PayKey = cb.SourceKey
", payDocKey);

            // =========================
            // IMPORTANT: WAIT until CB row exists
            // =========================
            bool cbReady = false;
            for (int t = 0; t < 15; t++) // ~3 seconds max (15 x 200ms)
            {
                int cbCount = AutoCount.Converter.ToInt32(
                    e.DBSetting.ExecuteScalar("SELECT COUNT(1) FROM CB WHERE SourceKey = ?", payDocKey)
                );

                if (cbCount > 0)
                {
                    cbReady = true;
                    break;
                }

                Thread.Sleep(200);
            }

            if (!cbReady) return; // CB still not created, skip silently

            // 4) Pass Member / StartDate / EndDate / LoanNo to CB (when OR created)
            // Fill only if CB field is empty
            e.DBSetting.ExecuteNonQuery(@"
UPDATE cb
SET
    cb.UDF_LOANNO =
        CASE
            WHEN (cb.UDF_LOANNO IS NULL OR LTRIM(RTRIM(cb.UDF_LOANNO)) = '')
             AND (src.UDF_LOANNO IS NOT NULL AND LTRIM(RTRIM(src.UDF_LOANNO)) <> '')
                THEN src.UDF_LOANNO
            ELSE cb.UDF_LOANNO
        END,

    cb.UDF_MEMBER =
        CASE
            WHEN (cb.UDF_MEMBER IS NULL OR LTRIM(RTRIM(cb.UDF_MEMBER)) = '')
             AND (src.UDF_MEMBER IS NOT NULL AND LTRIM(RTRIM(src.UDF_MEMBER)) <> '')
                THEN src.UDF_MEMBER
            ELSE cb.UDF_MEMBER
        END,

    cb.UDF_STARTDATE =
        CASE
            WHEN cb.UDF_STARTDATE IS NULL
             AND src.UDF_STARTDATE IS NOT NULL
                THEN src.UDF_STARTDATE
            ELSE cb.UDF_STARTDATE
        END,

    cb.UDF_ENDDATE =
        CASE
            WHEN cb.UDF_ENDDATE IS NULL
             AND src.UDF_ENDDATE IS NOT NULL
                THEN src.UDF_ENDDATE
            ELSE cb.UDF_ENDDATE
        END
FROM CB cb
OUTER APPLY (
    -- Take 1 invoice under this payment as source (ARInvoice already holds the UDF from Sales Invoice)
    SELECT TOP 1
        ar.UDF_LOANNO,
        ar.UDF_MEMBER,
        ar.UDF_STARTDATE,
        ar.UDF_ENDDATE
    FROM ARPaymentKnockOff k
    JOIN ARInvoice ar ON ar.DocKey = k.KnockOffDocKey
    WHERE k.DocKey = cb.SourceKey
    ORDER BY ar.DocKey DESC
) src
WHERE cb.SourceKey = ?
", payDocKey);
        }

        public void BeforeDelete(AutoCount.ARAP.ARPayment.ARPaymentBeforeDeleteEventArgs e)
        {
            if (e == null || e.MasterRecord == null) return;

            long payDocKey = AutoCount.Converter.ToInt64(e.MasterRecord.DocKey);
            if (payDocKey <= 0) return;

            string orNo = Convert.ToString(
                e.DBSetting.ExecuteScalar("SELECT DocNo FROM ARPayment WHERE DocKey = ?", payDocKey)
            ).Trim();

            if (string.IsNullOrWhiteSpace(orNo)) return;

            string csv = Convert.ToString(e.DBSetting.ExecuteScalar(@"
SELECT STUFF((
    SELECT ',' + CAST(KnockOffDocKey AS VARCHAR(20))
    FROM ARPaymentKnockOff
    WHERE DocKey = ?
    FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)')
, 1, 1, '')
", payDocKey) ?? "").Trim();

            if (csv == "") return;

            var invKeys = csv.Split(',')
                             .Select(x => x.Trim())
                             .Where(x => x != "")
                             .Select(x => AutoCount.Converter.ToInt64(x))
                             .Distinct()
                             .ToList();

            foreach (long invKey in invKeys)
            {
                string current = Convert.ToString(
                    e.DBSetting.ExecuteScalar("SELECT ISNULL(UDF_ORNO,'') FROM ARInvoice WHERE DocKey = ?", invKey)
                );

                string updated = RemoveOrNo(current, orNo);

                e.DBSetting.ExecuteNonQuery(
                    "UPDATE ARInvoice SET UDF_ORNO = ? WHERE DocKey = ?",
                    updated, invKey
                );

                e.DBSetting.ExecuteNonQuery(
                    "UPDATE ARInvoice SET UDF_PAID = 'F' WHERE DocKey = ?",
                    invKey
                );
            }

            e.DBSetting.ExecuteNonQuery(
                "UPDATE CB SET UDF_PAID = 'F' WHERE SourceKey = ?",
                payDocKey
            );

            e.DBSetting.ExecuteNonQuery(@"
UPDATE cb
SET cb.UDF_PAID = 'F'
FROM CB cb
JOIN ARPaymentKnockOff k ON k.DocKey = cb.SourceKey
WHERE k.KnockOffDocKey IN (
    SELECT KnockOffDocKey
    FROM ARPaymentKnockOff
    WHERE DocKey = ?
)
", payDocKey);
        }

        private static string RemoveOrNo(string existing, string removeOr)
        {
            removeOr = (removeOr ?? "").Trim();
            if (removeOr == "") return existing ?? "";

            if (string.IsNullOrWhiteSpace(existing))
                return "";

            var list = existing.Split(',')
                               .Select(x => x.Trim())
                               .Where(x => x != "")
                               .Where(x => !x.Equals(removeOr, StringComparison.OrdinalIgnoreCase))
                               .ToList();

            return string.Join(", ", list);
        }
    }
}
