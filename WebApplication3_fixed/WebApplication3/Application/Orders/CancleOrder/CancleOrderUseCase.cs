using WebApplication3.Application.Common;
using WebApplication3.Application.Common.Abstractions;
using WebApplication3.Domain;

namespace WebApplication3.Application.Orders.CancleOrder
{
    public sealed class CancelOrderUseCase
    {
        private readonly IOrderRepository _orders;
        private readonly IProductRepository _products;
        private readonly IUnitOfWork _uow;

        public CancelOrderUseCase(IOrderRepository orders, IProductRepository products, IUnitOfWork uow)
        {
            _orders = orders;
            _products = products;
            _uow = uow;
        }

        public async Task<Result<bool>> ExecuteAsync(CancelOrderCommand cmd, CancellationToken ct)
        {
            var order = await _orders.GetAsync(cmd.OrderId, ct);
            if (order is null) return Error.NotFound("order.not_found", "Заказ не найден");

            // Повторная отмена — ничего не делаем (и не возвращаем товар на склад второй раз)
            if (order.Status == OrderStatus.Cancelled) return true;

            try
            {
                order.Cancel();
            }
            catch (InvalidOperationException ex)
            {
                return Error.Conflict("order.cannot_cancel", ex.Message);
            }

            // Возвращаем товар на склад
            foreach (var item in order.Items)
            {
                var product = await _products.GetAsync(item.ProductId, ct);
                product?.IncreaseStock(item.Quantity);
            }

            await _uow.SaveChangesAsync(ct);   // раньше изменения не сохранялись
            return true;
        }
    }
}
