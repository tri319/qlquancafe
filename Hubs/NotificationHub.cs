using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace QLquancafe.Hubs
{
    /// <summary>
    /// Trung tâm truyền tải thông báo thời gian thực (SignalR Hub) cho hệ thống quản lý quán cà phê.
    /// Giúp đồng bộ hóa dữ liệu tức thời giữa khách hàng gọi món tại bàn, quầy pha chế và nhân viên phục vụ.
    /// </summary>
    public class NotificationHub : Hub
    {
        /// <summary>
        /// Gửi tín hiệu yêu cầu phục vụ từ khách hàng tại bàn.
        /// Khi khách hàng bấm nút gọi nhân viên, hệ thống sẽ phát tín hiệu này để màn hình quản trị của nhân viên hiển thị thông báo.
        /// </summary>
        /// <param name="tableId">ID của bàn yêu cầu phục vụ</param>
        /// <param name="tableName">Tên hiển thị của bàn (ví dụ: Bàn 1)</param>
        public async Task SendServiceCall(int tableId, string tableName)
        {
            await Clients.All.SendAsync("ReceiveServiceCall", tableId, tableName);
        }

        /// <summary>
        /// Gửi thông báo khi có đơn hàng (Order) mới được tạo thành công từ thiết bị của khách hàng hoặc nhân viên.
        /// Giúp quầy pha chế nhận được thông tin để chuẩn bị đồ uống ngay lập tức mà không cần in phiếu thủ công.
        /// </summary>
        /// <param name="orderId">Mã định danh hóa đơn mới tạo</param>
        /// <param name="tableName">Tên bàn tương ứng của hóa đơn</param>
        public async Task SendNewOrder(int orderId, string tableName)
        {
            await Clients.All.SendAsync("ReceiveNewOrder", orderId, tableName);
        }

        /// <summary>
        /// Cập nhật trạng thái pha chế của đơn hàng (ví dụ: Từ "Chờ pha chế" sang "Đang chuẩn bị" hoặc "Đã xong").
        /// Giúp đồng bộ trạng thái trên bảng điều khiển Kanban của bếp/bar và màn hình của nhân viên phục vụ.
        /// </summary>
        /// <param name="orderId">Mã định danh hóa đơn cần cập nhật</param>
        /// <param name="status">Trạng thái pha chế mới (Pending, Preparing, Ready)</param>
        public async Task UpdateOrderStatus(int orderId, string status)
        {
            await Clients.All.SendAsync("ReceiveOrderStatusUpdate", orderId, status);
        }

        /// <summary>
        /// Phát thông báo yêu cầu nhân viên phục vụ đến quầy bar lấy đồ uống đã làm xong để mang ra cho khách.
        /// Kích hoạt khi quầy pha chế bấm nút "Hoàn thành" trên bảng điều khiển.
        /// </summary>
        /// <param name="orderId">Mã định danh hóa đơn đã làm xong</param>
        /// <param name="tableName">Tên bàn cần phục vụ đồ uống</param>
        public async Task NotifyWaiter(int orderId, string tableName)
        {
            await Clients.All.SendAsync("ReceiveWaiterNotification", orderId, tableName);
        }

        /// <summary>
        /// Phát tín hiệu khi đơn hàng được thanh toán thành công (bằng tiền mặt qua nhân viên hoặc tự thanh toán qua VNPAY).
        /// Giúp thiết bị của khách hàng tại bàn tự động làm mới (Reload) để cập nhật trạng thái hóa đơn đã trả tiền.
        /// </summary>
        /// <param name="orderId">Mã định danh hóa đơn vừa thanh toán xong</param>
        public async Task SendOrderPaid(int orderId)
        {
            await Clients.All.SendAsync("ReceiveOrderPaid", orderId);
        }
    }
}
