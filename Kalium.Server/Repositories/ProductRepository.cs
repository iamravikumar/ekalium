﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Kalium.Server.Context;
using Kalium.Shared.Consts;
using Kalium.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MoreLinq;

namespace Kalium.Server.Repositories
{
    internal class ProductSearchHelper : SearchHelper<Product>
    {
        public ProductSearchHelper(ApplicationDbContext context) : base(context)
        {
            Collection = context.Products;
        }
        public ProductSearchHelper IncludeImages()
        {
            Collection = Collection.Include(p => p.Images);
            return this;
        }
        public ProductSearchHelper IncludeReviews()
        {
            Collection = Collection.Include(p => p.Reviews);
            return this;
        }
        public ProductSearchHelper IncludeAuctions()
        {
            Collection = Collection.Include(p => p.Auctions);
            return this;
        }
        public ProductSearchHelper IncludeDiscussions()
        {
            Collection = Collection.Include(p => p.Discussions);
            return this;
        }
        public ProductSearchHelper IncludeCategory()
        {
            Collection = Collection.Include(p => p.Category);
            return this;
        }
        public ProductSearchHelper IncludeOrderItems()
        {
            Collection = Collection.Include(p => p.OrderItems);
            return this;
        }
        public ProductSearchHelper IncludeExtras()
        {
            Collection = Collection
                .Include(p => p.Extras)
                .ThenInclude(extra => extra.Options);
            return this;
        }
        public ProductSearchHelper IncludeCoupons()
        {
            Collection = Collection
                .Include(p => p.Coupons);
            return this;
        }
        public ProductSearchHelper HasOrigin(ICollection<string> origins)
        {
            if (origins.Any())
            {
                Collection = Collection.Where(pro => origins.Contains(pro.Origin.Trim()));
            }
            return this;
        }
        public ProductSearchHelper HasMaterial(ICollection<string> materials)
        {
            if (materials.Any())
            {
                Collection = Collection.Where(pro => materials.Contains(pro.Material));
            }
            return this;
        }
        public ProductSearchHelper SortBy(Consts.SortType sortType)
        {
            Expression<Func<Product, IComparable>> comparator = p => p.Name;
            switch (sortType)
            {
                case Consts.SortType.Newness:
                    comparator = p => p.DateCreated;
                    break;
                case Consts.SortType.Popularity:
                    comparator = p => p.QuantitySold;
                    break;
                case Consts.SortType.Rating:
                    comparator = p => p.AverageRating;
                    break;
                case Consts.SortType.Price:
                    comparator = p => -p.DiscountedPrice;
                    break;
            }

            Collection = Collection.OrderByDescending(comparator);
            return this;
        }
        public ProductSearchHelper FromCategory(int categoryId)
        {
            if (categoryId != Consts.NoPreference)
            {
                Collection = Collection.Where(pro => pro.Category.Id == categoryId);
            }
            return this;
        }
        public ProductSearchHelper FromPrice(double minimumPrice)
        {
            if (Math.Abs(minimumPrice - Consts.NoPreference) > 0)
            {
                Collection = Collection.Where(pro => pro.DiscountedPrice >= minimumPrice);
            }
            return this;
        }
        public ProductSearchHelper ToPrice(double maximumPrice)
        {
            if (Math.Abs(maximumPrice - Consts.NoPreference) > 0)
            {
                Collection = Collection.Where(pro => pro.DiscountedPrice <= maximumPrice);
            }
            return this;
        }
        public ProductSearchHelper WithStatus(int status)
        {
            Collection = Collection.Where(pro => pro.Status == status);
            return this;
        }

        public ProductSearchHelper FromCategory(string category)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                Collection = Collection.Where(pro => pro.Category.Name.Equals(category, StringComparison.CurrentCultureIgnoreCase));
            }
            return this;
        }

        public async Task<Product> WithId(int id)
        {
            return await Collection.SingleOrDefaultAsync(pro => pro.Id == id);
        }

        public ProductSearchHelper HasBrand(ICollection<int> brands)
        {
            if (brands.Any())
            {
                Collection = Collection.Where(pro => brands.Contains(pro.Brand.Id));
            }
            return this;
        }
    }

    public interface IProductRepository
    {
        Task<Product> FindProductById(int id);
        Task<Product> FindProductByUrl(string url);
        Task<ICollection<Product>> SearchProducts(int page, int pageSize, string category, double minPrice,
            double maxPrice, int status, ICollection<string> origins, ICollection<string> materials,
            ICollection<int> brands, int sortType);
        Task<Product> AddProduct(Product product);
        Product UpdateProduct(Product product);
        Task<ICollection<Product>> SearchProducts();
        Task<int> CountProducts(string category, double minPrice, double maxPrice, int status,
            ICollection<string> origins, ICollection<string> materials, ICollection<int> brands);
        Task<ICollection<string>> GetOrigins(int top);
        Task<ICollection<string>> GetMaterials(int top);
        Task<Product> FindProductByIdForCart(int id);
        Task<ICollection<Brand>> GetBrands();
        Task<Product> FindProductByIdForCartNoFreshen(int id);
    }

    public class ProductRepository: IProductRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public ProductRepository(ApplicationDbContext ctx, IMemoryCache cache)
        {
            _context = ctx;
            _cache = cache;
        }

        public async Task<Product> FindProductById(int id)
        {
            var cacheKey = Consts.GetCachePrefix(Consts.CachePrefix.ProductId, id);
            if (_cache.TryGetValue(cacheKey, out var product))
            {
                return FreshenProduct(product as Product);
            }
            else
            {
                var newProduct = FreshenProduct(await _context.Products.FindAsync(id));
                _cache.Set(cacheKey, newProduct);
                return newProduct;
            }
        }

        private Product FreshenProduct(Product newProduct)
        {
            newProduct.Extras = newProduct.Extras?.Where(p => !p.Deleted).ToList();
            newProduct.Extras?.ForEach(ext => { ext.Options = ext.Options.Where(opt => !opt.Deleted).ToList(); });
            newProduct.Coupons = newProduct.Coupons?.Where(c => !c.Deleted).ToList();
            newProduct.Reviews = newProduct.Reviews?.Where(r => !r.Deleted).ToList();
            newProduct.Reviews = newProduct.Reviews?.Where(r => !r.Deleted).ToList();
            newProduct.Auctions = newProduct.Auctions?.Where(a => a.Status == (int) Consts.Status.Public).ToList();
            return newProduct;
        }

        public async Task<Product> FindProductByUrl(string url)
        {
            var cacheKey = Consts.GetCachePrefix(Consts.CachePrefix.ProductUrl, url);
            if (_cache.TryGetValue(cacheKey, out var product))
            {
                return FreshenProduct(product as Product);
            }
            else
            {
                var newProduct = await _context.Products
                    .Where(p => p.Status == (int)Consts.Status.Public)
                    .Include(p => p.Category)
                    .Include(p => p.Images)
                    .Include(p => p.Reviews)
                    .Include(p => p.Discussions)
                    .Include(p => p.Extras)
                    .ThenInclude(extra => extra.Options)
                    .FirstOrDefaultAsync(p => p.NameUrl.Equals(url, StringComparison.CurrentCultureIgnoreCase));
                newProduct = FreshenProduct(newProduct);
                _cache.Set(cacheKey, newProduct);
                return newProduct;
            }
        }

        public async Task<ICollection<Product>> SearchProducts() => await _context.Products.ToListAsync();
        public async Task<ICollection<Product>> SearchProducts(int page, int pageSize, string category, double minPrice,
            double maxPrice, int status, ICollection<string> origins, ICollection<string> materials,
            ICollection<int> brands, int sortType)
        {
            var searcher = new ProductSearchHelper(_context);
            return await searcher
                .FromCategory(category)
                .FromPrice(minPrice)
                .ToPrice(maxPrice)
                .WithStatus(status)
                .HasOrigin(origins)
                .HasMaterial(materials)
                .HasBrand(brands)
                .SortBy((Consts.SortType) sortType)
                .IncludeCategory()
                .IncludeImages()
                .Page(page, pageSize)
                .Get();
        }
        public async Task<Product> AddProduct(Product product)
        {
            var entityEntry = await _context.Products.AddAsync(product);
            return entityEntry.Entity;
        }
        public Product UpdateProduct(Product product)
        {
            var entityEntry = _context.Products.Update(product);
            return entityEntry.Entity;
        }
        public async Task<int> CountProducts(string category, double minPrice, double maxPrice, int status,
            ICollection<string> origins, ICollection<string> materials, ICollection<int> brands)
        {
            var searcher = new ProductSearchHelper(_context);
            return await searcher
                .FromCategory(category)
                .FromPrice(minPrice)
                .ToPrice(maxPrice)
                .WithStatus(status)
                .HasOrigin(origins)
                .HasMaterial(materials)
                .HasBrand(brands)
                .Count();
        }
        public async Task<ICollection<string>> GetOrigins(int top)
        {
            return await _context.Products.Select(pro => pro.Origin).Distinct()
                .OrderByDescending(attr => _context.Products.Count(pro => pro.Origin.Equals(attr))).Take(top).ToListAsync();
        }
        public async Task<ICollection<string>> GetMaterials(int top)
        {
            return await _context.Products.Select(pro => pro.Material.Trim()).Distinct()
                .OrderByDescending(attr => _context.Products.Count(pro => pro.Material.Equals(attr))).Take(top).ToListAsync();
        }

        public async Task<Product> FindProductByIdForCart(int id)
        {
            var helper = new ProductSearchHelper(_context);
            var p = await helper
                .IncludeImages()
                .IncludeExtras()
                .IncludeCoupons()
                .WithId(id);
            return FreshenProduct(p);
        }

        public async Task<Product> FindProductByIdForCartNoFreshen(int id)
        {
            var helper = new ProductSearchHelper(_context);
            var p = await helper
                .IncludeImages()
                .IncludeExtras()
                .IncludeCoupons()
                .WithId(id);
            return p;
        }

        public async Task<ICollection<Brand>> GetBrands()
        {
            return await _context.Brand.OrderBy(brand => brand.Name).ToListAsync();
        }
    }
}
