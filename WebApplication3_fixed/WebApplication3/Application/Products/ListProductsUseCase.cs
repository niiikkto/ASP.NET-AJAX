using WebApplication3.Application.Common.Abstractions;

namespace WebApplication3.Application.Products
{
    public sealed record ProductDto(Guid Id, string Name, decimal Price, int Stock);

    public sealed class ListProductsUseCase
    {
        private readonly IProductRepository _products;
        public ListProductsUseCase(IProductRepository products) => _products = products;

        public async Task<IReadOnlyList<ProductDto>> ExecuteAsync(CancellationToken ct)
        {
            var list = await _products.ListAsync(ct);
            return list.Select(p => new ProductDto(p.Id, p.Name, p.Price, p.Stock)).ToList();
        }
    }
}
