using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QLquancafe.Data;
using QLquancafe.Helpers;

namespace QLquancafe.Controllers
{
    public class PaymentController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public PaymentController(IConfiguration configuration, ApplicationDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        public async Task<IActionResult> CreatePaymentUrl(int orderId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null || order.IsPaid)
            {
                var sessionTableId = HttpContext.Session.GetInt32("CurrentTableId");
                if (sessionTableId.HasValue)
                {
                    return RedirectToAction("Create", "Orders", new { tableId = sessionTableId.Value });
                }
                return RedirectToAction("Index", "Home");
            }

            var url = _configuration["Vnpay:BaseUrl"];
            var returnUrl = _configuration["Vnpay:ReturnUrl"];
            var tmnCode = _configuration["Vnpay:TmnCode"];
            var hashSecret = _configuration["Vnpay:HashSecret"];

            VnPayLibrary pay = new VnPayLibrary();

            pay.AddRequestData("vnp_Version", "2.1.0");
            pay.AddRequestData("vnp_Command", "pay");
            pay.AddRequestData("vnp_TmnCode", tmnCode);
            pay.AddRequestData("vnp_Amount", (order.TotalAmount * 100).ToString("0")); 
            pay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            pay.AddRequestData("vnp_CurrCode", "VND");
            pay.AddRequestData("vnp_IpAddr", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1");
            pay.AddRequestData("vnp_Locale", "vn");
            pay.AddRequestData("vnp_OrderInfo", "Thanh toan don hang: " + order.Id);
            pay.AddRequestData("vnp_OrderType", "other"); // default
            pay.AddRequestData("vnp_ReturnUrl", returnUrl);
            pay.AddRequestData("vnp_TxnRef", order.Id.ToString() + "_" + DateTime.Now.Ticks.ToString()); 

            string paymentUrl = pay.CreateRequestUrl(url, hashSecret);

            return Redirect(paymentUrl);
        }

        public async Task<IActionResult> PaymentCallback()
        {
            if (Request.Query.Count > 0)
            {
                string hashSecret = _configuration["Vnpay:HashSecret"]; 
                var vnpayData = Request.Query;
                VnPayLibrary pay = new VnPayLibrary();

                foreach (var s in vnpayData)
                {
                    if (!string.IsNullOrEmpty(s.Key) && s.Key.StartsWith("vnp_"))
                    {
                        pay.AddResponseData(s.Key, s.Value.ToString());
                    }
                }

                string orderIdAndTicks = pay.GetResponseData("vnp_TxnRef");
                long vnp_Amount = Convert.ToInt64(pay.GetResponseData("vnp_Amount")) / 100;
                string vnp_ResponseCode = pay.GetResponseData("vnp_ResponseCode");
                string vnp_SecureHash = Request.Query["vnp_SecureHash"];

                bool checkSignature = pay.ValidateSignature(vnp_SecureHash, hashSecret);

                if (checkSignature)
                {
                    if (vnp_ResponseCode == "00")
                    {
                        // Thanh toán thành công
                        int orderId = int.Parse(orderIdAndTicks.Split('_')[0]);
                        var order = await _context.Orders.FindAsync(orderId);
                        if (order != null && !order.IsPaid)
                        {
                            order.IsPaid = true;
                            _context.Update(order);

                            var table = await _context.Tables.FindAsync(order.TableId);
                            if (table != null)
                            {
                                table.Status = "Trống";
                            }

                            await _context.SaveChangesAsync();
                        }
                        ViewBag.Message = "Thanh toán thành công đơn hàng " + orderId;
                        return View("PaymentSuccess");
                    }
                    else
                    {
                        // Lỗi thanh toán
                        ViewBag.Message = "Có lỗi xảy ra trong quá trình xử lý giao dịch. Mã lỗi: " + vnp_ResponseCode;
                        return View("PaymentFail");
                    }
                }
                else
                {
                    ViewBag.Message = "Có lỗi xảy ra trong quá trình xử lý: Chữ ký không hợp lệ.";
                    return View("PaymentFail");
                }
            }

            var sessionTableId = HttpContext.Session.GetInt32("CurrentTableId");
            if (sessionTableId.HasValue)
            {
                return RedirectToAction("Create", "Orders", new { tableId = sessionTableId.Value });
            }
            return RedirectToAction("Index", "Home");
        }
    }
}
