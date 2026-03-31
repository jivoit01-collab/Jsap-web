using Microsoft.Data.SqlClient;
using System.Data;

public class InvoicePaymentService

{
    private readonly IConfiguration _config;

    public InvoicePaymentService(IConfiguration config)
    {
        _config = config;
    }

    public List<object> GetBillDetails(DateTime? fromDate, DateTime? toDate, string accountName, decimal? serialNumber)
    {
        List<object> data = new List<object>();

        string connStr = _config.GetConnectionString("FHConnection");

        using (SqlConnection conn = new SqlConnection(connStr))
        {
            using (SqlCommand cmd = new SqlCommand("GetBillDetails", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                // 🔥 Add timeout (VERY IMPORTANT)
                cmd.CommandTimeout = 120;

                // 🔥 Better parameter handling
                cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = (object)fromDate ?? DBNull.Value;
                cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = (object)toDate ?? DBNull.Value;
                cmd.Parameters.Add("@AccountName", SqlDbType.NVarChar).Value =
                    string.IsNullOrWhiteSpace(accountName) ? DBNull.Value : accountName;
                cmd.Parameters.Add("@SerialNumber", SqlDbType.Decimal).Value = (object)serialNumber ?? DBNull.Value;

                conn.Open();

                // 🔥 Wrap reader in using (CRITICAL FIX)
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        data.Add(new
                        {
                            accountName = reader["AccountName"]?.ToString(),
                            vchNumber = reader["VchNumber"],
                            voucherDate = reader["VoucherDate"],
                            billAmount = reader["BillAmount"],

                            attachmentPath = reader["AttachmentPath"] == DBNull.Value
                                ? null
                                : reader["AttachmentPath"].ToString(),

                            makerRemark = reader["MakerRemark"] == DBNull.Value
                                ? null
                                : reader["MakerRemark"].ToString(),

                            supplierRef = reader["SupplierRef"]?.ToString(),
                            supplierRefDate = reader["SupplierRefDate"] == DBNull.Value ? null : reader["SupplierRefDate"],
                            dueDate = reader["DueDate"] == DBNull.Value ? null : reader["DueDate"],

                            checkerStatus = reader["CheckerStatus"]?.ToString(),
                            checkerRemark = reader["CheckerRemark"]?.ToString(),
                            paymentStatus = reader["PaymentStatus"]?.ToString()
                        });
                    }
                }
            }
        }

        return data;
    }
    public List<object> GetInvoiceItemDetails(int vchNumber)
    {
        List<object> items = new List<object>();

        string connStr = _config.GetConnectionString("FHConnection");

        using (SqlConnection conn = new SqlConnection(connStr))
        {
            using (SqlCommand cmd = new SqlCommand("GetInvoice", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 120;

                // ✅ Sahi parameter pass ho raha hai
                cmd.Parameters.Add("@VchNumber", SqlDbType.Decimal).Value = vchNumber;
                cmd.Parameters.Add("@SerialNumber", SqlDbType.Decimal).Value = DBNull.Value;
                cmd.Parameters.Add("@AccountName", SqlDbType.NVarChar).Value = DBNull.Value;
                cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = DBNull.Value;
                cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = DBNull.Value;

                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    // ✅ First result set skip karo (header data)
                    reader.NextResult();

                    // ✅ Second result set = product details
                    while (reader.Read())
                    {
                        items.Add(new
                        {
                            productName = reader["ProductName"]?.ToString(),
                            quantity = reader["Quantity"],
                            rate = reader["PurchaseCost"],
                            tax = reader["TaxRate"],
                            amount = reader["ItemValue"]
                        });
                    }
                }
            }
        }

        return items;
    }
}

