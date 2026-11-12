using Microsoft.AspNetCore.Mvc;
using WebApplication3.Application.Orders.CancleOrder;
using WebApplication3.Application.Orders.CreateOrder;
using WebApplication3.Application.Orders.GetOrder;
using WebApplication3.Application.Products;
using WebApplication3.Domain;
using WebApplication3.Web.Extensions;
using WebApplication3.Web.Models;

namespace WebApplication3.Web.Controllers
{
    public class OrdersController : Controller
    {
        private readonly CreateOrderUseCase _createOrder;
        private readonly GetOrderUseCase _getOrder;
        private readonly CancelOrderUseCase _cancelOrder;
        private readonly ListProductsUseCase _listProducts;

        public OrdersController(
            CreateOrderUseCase createOrder,
            GetOrderUseCase getOrder,
            CancelOrderUseCase cancelOrder,
            ListProductsUseCase listProducts)
        {
            _createOrder = createOrder;
            _getOrder = getOrder;
            _cancelOrder = cancelOrder;
            _listProducts = listProducts;
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken ct)
        {
            await LoadProductsAsync(ct);
            // одна пустая строка сразу, чтобы форму можно было отправить
            return View(new CreateOrderViewModel { Items = { new CreateOrderItemViewModel() } });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateOrderViewModel vm, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                await LoadProductsAsync(ct);
                return View(vm);
            }

            // ViewModel -> Command
            var command = new CreateOrderCommand(
                vm.CustomerEmail,
                vm.Items.Select(i => new CreateOrderItem(i.ProductId, i.Quantity)).ToList());

            var result = await _createOrder.ExecuteAsync(command, ct);

            if (!result.IsSuccess)
            {
                // бизнес-ошибку показываем на форме, а не сырым JSON
                ModelState.AddModelError(string.Empty, result.Error!.Message);
                await LoadProductsAsync(ct);
                return View(vm);
            }

            return RedirectToAction(nameof(Details), new { id = result.Value });
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id, CancellationToken ct)
        {
            var result = await _getOrder.ExecuteAsync(new GetOrderQuery(id), ct);

            return result.ToActionResult(this, dto => View(new OrderDetailsViewModel
            {
                Id = dto.Id,
                CustomerEmail = dto.CustomerEmail,
                Status = dto.Status.ToString(),
                Total = dto.Total,
                CreatedAtUtc = dto.CreatedAtUtc,
                CanBeCancelled = dto.Status == OrderStatus.Created,
                Items = dto.Items.Select(i => new OrderItemViewModel
                {
                    ProductId = i.ProductId,
                    Name = i.Name,
                    Price = i.Price,
                    Quantity = i.Quantity
                }).ToList()
            }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        {
            var result = await _cancelOrder.ExecuteAsync(new CancelOrderCommand(id), ct);

            if (!result.IsSuccess)
                TempData["Error"] = result.Error!.Message;

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task LoadProductsAsync(CancellationToken ct)
            => ViewBag.Products = await _listProducts.ExecuteAsync(ct);
    }
}
