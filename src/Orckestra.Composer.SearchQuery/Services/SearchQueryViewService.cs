﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orckestra.Composer.Parameters;
using Orckestra.Composer.Providers;
using Orckestra.Composer.Providers.Dam;
using Orckestra.Composer.Repositories;
using Orckestra.Composer.Search;
using Orckestra.Composer.Search.Facets;
using Orckestra.Composer.Search.Factory;
using Orckestra.Composer.Search.Parameters;
using Orckestra.Composer.Search.Repositories;
using Orckestra.Composer.Search.Services;
using Orckestra.Composer.Search.ViewModels;
using Orckestra.Composer.SearchQuery.Extensions;
using Orckestra.Composer.SearchQuery.Parameters;
using Orckestra.Composer.SearchQuery.Providers;
using Orckestra.Composer.SearchQuery.Repositories;
using Orckestra.Composer.SearchQuery.ViewModels;
using Orckestra.Composer.Services;
using Orckestra.Composer.Utils;
using Orckestra.Overture.ServiceModel;
using Orckestra.Overture.ServiceModel.Products.Inventory;
using Orckestra.Overture.ServiceModel.Search;
using Orckestra.Overture.ServiceModel.Search.Pricing;
using static Orckestra.Composer.Utils.MessagesHelper.ArgumentException;

namespace Orckestra.Composer.SearchQuery.Services
{
    public class SearchQueryViewService : BaseSearchViewService<SearchParam>, ISearchQueryViewService
    {
        protected ISearchQueryRepository SearchQueryRepository { get; }
       protected ISearchQueryUrlProvider SearchQueryUrlProvider { get; }
        protected Repositories.IInventoryRepository InventoryRepository { get; private set; }
        protected IProductSettingsRepository ProductSettingsRepository { get; private set; }

        public SearchQueryViewService(
         ICategoryRepository categoryRepository,
         ISearchRepository searchRepository,
            IDamProvider damProvider,
            ILocalizationProvider localizationProvider,
            ISearchUrlProvider searchUrlProvider,
            IFacetFactory facetFactory,
            ISelectedFacetFactory selectedFacetFactory,
            IComposerContext composerContext,
            ISearchQueryRepository searchQueryRepository,
            ISearchQueryUrlProvider searchQueryUrlProvider,
            IProductSettingsRepository productSettingsRepository,
            Repositories.IInventoryRepository inventoryRepository,
            IProductSearchViewModelFactory productSearchViewModelFactory)
         : base(
            searchRepository,
            damProvider,
            localizationProvider,
            searchUrlProvider,
            facetFactory,
            selectedFacetFactory,
            composerContext,
            productSearchViewModelFactory,
            categoryRepository)
        {
            SearchQueryRepository = searchQueryRepository ?? throw new ArgumentNullException(nameof(searchQueryRepository));
            SearchQueryUrlProvider = searchQueryUrlProvider ?? throw new ArgumentNullException(nameof(searchQueryUrlProvider));
            ProductSettingsRepository = productSettingsRepository;
            InventoryRepository = inventoryRepository;
        }

        private const string VariantPropertyBagKey = "VariantId";

        public async Task<SearchQueryViewModel> GetSearchQueryViewModelAsync(GetSearchQueryViewModelParams param)
        {
            SearchQueryViewModel viewModel;

            if (param == null) { throw new ArgumentNullException(nameof(param)); }
            if (param.CultureInfo == null) { throw new ArgumentException(GetMessageOfNull(nameof(param.CultureInfo)), nameof(param)); }
            if (param.Criteria == null) { throw new ArgumentException(GetMessageOfNull(nameof(param.Criteria)), nameof(param)); }

            var searchQueryProducts = await SearchQueryRepository.SearchQueryProductAsync(new SearchQueryProductParams
            {
                CultureName = param.CultureInfo.Name,
                QueryName = param.QueryName,
                QueryType = param.QueryType,
                ScopeId = param.Scope,
                Criteria = param.Criteria
            }).ConfigureAwait(false);

            var documents = searchQueryProducts.Result.Documents.Select(ToProductDocument).ToList();

            var inventoryLocations = await InventoryRepository.GetInventoryLocationStatusesBySkus(
                new GetInventoryLocationStatuseParam()
                {
                    Skus = documents.Select(d => d.Sku).ToList(),
                    ScopeId = param.Scope,
                    InventoryLocationIds = param.InventoryLocationIds

                }).ConfigureAwait(false);

            FixInventories(documents, inventoryLocations);
            documents = await FixInventoryFilter(documents, param.Scope).ConfigureAwait(false);

            var getImageParam = new GetProductMainImagesParam
            {
                ImageSize = SearchConfiguration.DefaultImageSize,
                ProductImageRequests = documents
                    .Select(document => new ProductImageRequest
                    {
                        ProductId = document.ProductId,
                        Variant = document.PropertyBag.ContainsKey(VariantPropertyBagKey)
                            ? new VariantKey { Id = document.PropertyBag[VariantPropertyBagKey].ToString() }
                            : VariantKey.Empty,
                        ProductDefinitionName = document.PropertyBag.ContainsKey("DefinitionName")
                            ? document.PropertyBag["DefinitionName"].ToString()
                            : string.Empty,
                        PropertyBag = document.PropertyBag
                    }).ToList()
            };

            var imageUrls = await DamProvider.GetProductMainImagesAsync(getImageParam).ConfigureAwait(false);

            var newCriteria = param.Criteria.Clone();

            var createSearchViewModelParam = new CreateProductSearchResultsViewModelParam<SearchParam>
            {
                SearchParam = new SearchParam()
                {
                    Criteria = newCriteria
                },
                ImageUrls = imageUrls,
                SearchResult = new ProductSearchResult()
                {
                    Documents = documents,
                    TotalCount = searchQueryProducts.Result.TotalCount,
                    Facets = searchQueryProducts.Result.Facets,
                },
            };

            if (param.QueryType != Overture.ServiceModel.SearchQueries.SearchQueryType.ProductSet &&
                param.Criteria.IncludeFacets &&
               param.Criteria.SelectedFacets != null &&
               param.Criteria.SelectedFacets.Any(s => s.Name?.StartsWith(SearchConfiguration.CategoryFacetFiledNamePrefix) ?? false))
            {
                createSearchViewModelParam.CategoryFacetCountsResult = await SearchQueryRepository.GetCategoryFacetCountsAsync(param.Criteria, searchQueryProducts).ConfigureAwait(false);
            }

            viewModel = new SearchQueryViewModel
            {
                QueryName = param.QueryName,
                QueryType = param.QueryType,
                FacetSettings = new FacetSettingsViewModel()
                {
                    SelectedFacets =
                    await GetSelectedFacetsAsync(createSearchViewModelParam.SearchParam).ConfigureAwait(false),
                },
                ProductSearchResults =
                    await CreateProductSearchResultsViewModelAsync(createSearchViewModelParam).ConfigureAwait(false),
            };

            ProcessFacets(viewModel, searchQueryProducts);

            viewModel.FacetSettings.CategoryFacetValuesTree = await BuildCategoryFacetValuesTree(viewModel.ProductSearchResults.Facets,
              viewModel.FacetSettings.SelectedFacets,
              viewModel.ProductSearchResults.CategoryFacetCounts).ConfigureAwait(false);

            if (viewModel.FacetSettings.CategoryFacetValuesTree != null)
            {
                viewModel.FacetSettings.CategoryFacetValuesTree.TotalCount = createSearchViewModelParam.CategoryFacetCountsResult != null ? createSearchViewModelParam.CategoryFacetCountsResult.TotalCount : viewModel.ProductSearchResults.TotalCount;
                viewModel.FacetSettings.Context["CategoryFacetValuesTree"] = viewModel.FacetSettings.CategoryFacetValuesTree;
            }

            // Json context for Facets
            viewModel.FacetSettings.Context["SelectedFacets"] = viewModel.FacetSettings.SelectedFacets;
            viewModel.FacetSettings.Context["Facets"] = viewModel.ProductSearchResults.Facets.Where(f => !f.FieldName.StartsWith(SearchConfiguration.CategoryFacetFiledNamePrefix));
            viewModel.FacetSettings.Context["PromotedFacetValues"] = viewModel.ProductSearchResults.PromotedFacetValues;

            viewModel.Context[nameof(viewModel.ProductSearchResults.SearchResults)] = viewModel.ProductSearchResults.SearchResults;
            viewModel.Context[nameof(SearchConfiguration.MaxItemsPerPage)] = SearchConfiguration.MaxItemsPerPage;
            viewModel.Context["ListName"] = "Search Query";

            return viewModel;
        }

        protected virtual void ProcessFacets(SearchQueryViewModel viewModel, Overture.ServiceModel.SearchQueries.SearchQueryResult searchQueryProducts)
        {
            if (searchQueryProducts.SelectedFacets == null) return;

            foreach (var selectedFacet in searchQueryProducts.SelectedFacets)
            {
                foreach (var value in selectedFacet.Values)
                {
                    var isQuerySelectedFacet = viewModel.FacetSettings.SelectedFacets.Facets.All(f => f.Value != value);
                    if (isQuerySelectedFacet)
                    {
                        viewModel.FacetSettings.SelectedFacets.Facets.Add(new SelectedFacet()
                        {
                            Value = value,
                            FieldName = selectedFacet.FacetName,
                            DisplayName = value,
                            IsRemovable = false
                        });
                    }
                }

                var querySelectedFacets = viewModel.FacetSettings.SelectedFacets.Facets.Where(f => !f.IsRemovable).ToList();

                var modelFacets = viewModel.ProductSearchResults.Facets.Where(d => d.FieldName == selectedFacet.FacetName);
                foreach (var facet in modelFacets)
                {
                    var facetValues = facet?.FacetValues.Concat(facet.OnDemandFacetValues);
                    var selectedFacetValues = selectedFacet.Values.Select(value => facetValues.FirstOrDefault(f => f.Value == value)).Where(facetValue => facetValue != null);
                    foreach (var facetValue in selectedFacetValues)
                    {
                        facetValue.IsSelected = true;
                        var isQuerySelected = querySelectedFacets.FirstOrDefault(f => f.Value == facetValue.Value);
                        if (isQuerySelected != null)
                        {
                            facetValue.IsRemovable = false;
                        }
                    }

                    var isFacetFieldSelectedInQuery = querySelectedFacets.Any(qf => qf.FieldName == facet.FieldName);
                    if(isFacetFieldSelectedInQuery && facet.FacetType == Search.Facets.FacetType.SingleSelect)
                    {
                        //need to clean other facet values for this facet field to not allow multiselect
                        facet.FacetValues = facet.FacetValues.Where(fv => querySelectedFacets.Any(qf => qf.Value == fv.Value && qf.FieldName == facet.FieldName)).ToList();
                        facet.OnDemandFacetValues = facet.OnDemandFacetValues.Where(fv => querySelectedFacets.Any(qf => qf.Value == fv.Value && qf.FieldName == facet.FieldName)).ToList();

                    }
                }
            }
        }

        private void FixInventories(List<ProductDocument> documents, List<InventoryItemAvailability> inventoryLocations)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            if (inventoryLocations == null) throw new ArgumentNullException(nameof(inventoryLocations));

            var lookup = inventoryLocations
                .Where(x=> x.Identifier != null)
                .ToLookup(el => el.Identifier?.Sku, el => el, StringComparer.OrdinalIgnoreCase);

            foreach (var productDocument in documents)
            {
                productDocument.InventoryLocationStatuses = lookup[productDocument.Sku].ToList();
            }
        }

        private async Task<List<ProductDocument>> FixInventoryFilter(List<ProductDocument> documents, string scope)
        {
            var productSettings = await ProductSettingsRepository.GetProductSettings(scope).ConfigureAwait(false);
            if (productSettings.IsInventoryEnabled)
            {
                var result = new List<ProductDocument>();
                var availableInventoryStatuses = new HashSet<InventoryStatus>();

                foreach (var s in productSettings.AvailableInventoryStatuses.Split('|'))
                {
                    if (Enum.TryParse(s, out InventoryStatus status))
                    {
                        availableInventoryStatuses.Add(status);
                    }
                }

                foreach (var productDocument in documents)
                {
                    bool nextDocument = false;
                    foreach(var inventoryLocationStatus in productDocument.InventoryLocationStatuses)
                    {
                        foreach(var inventoryItemStatus in inventoryLocationStatus.Statuses)
                        {
                            if (availableInventoryStatuses.Contains(inventoryItemStatus.Status))
                            {
                                result.Add(productDocument);
                                nextDocument = true;
                                break;
                            }
                        }
                        if (nextDocument) break;
                    }
                }
                return result;
            }
            else
            {
                return documents;
            }
        }


        protected virtual async Task<SelectedFacets> GetSelectedFacetsAsync(SearchParam param)
        {
            var selectedFacets = param.Criteria.SelectedFacets;
            return await Task.FromResult(FlattenFilterList(selectedFacets, param.Criteria.CultureInfo));
        }

        public ProductDocument ToProductDocument(Document document)
        {
            if (document == null) return null;
            var productDoc = new ProductDocument();

            if (document.PropertyBag != null)
            {
                productDoc.PropertyBag = new PropertyBag(document.PropertyBag);

                if (document.PropertyBag.ContainsKey(nameof(productDoc.Id)))
                    productDoc.Id = (string)document.PropertyBag[nameof(productDoc.Id)];
                if (document.PropertyBag.ContainsKey(nameof(productDoc.CatalogId)))
                    productDoc.CatalogId = (string)document.PropertyBag[nameof(productDoc.CatalogId)];
                if (document.PropertyBag.ContainsKey(nameof(productDoc.ProductId)))
                    productDoc.ProductId = (string)document.PropertyBag[nameof(productDoc.ProductId)];
                if (document.PropertyBag.ContainsKey(nameof(productDoc.Sku)))
                    productDoc.Sku = (string)document.PropertyBag[nameof(productDoc.Sku)];

                productDoc.InventoryLocationStatuses = new List<InventoryItemAvailability>();

                //Legacy pricing fields

                #pragma warning disable 612, 618
                if (document.PropertyBag.ContainsKey(nameof(productDoc.CurrentPrice)) && document.PropertyBag[nameof(productDoc.CurrentPrice)] != null)
                    productDoc.CurrentPrice = double.Parse(document.PropertyBag[nameof(productDoc.CurrentPrice)].ToString());
                if (document.PropertyBag.ContainsKey(nameof(productDoc.DefaultPrice)) && document.PropertyBag[nameof(productDoc.DefaultPrice)] != null)
                    productDoc.DefaultPrice = double.Parse(document.PropertyBag[nameof(productDoc.DefaultPrice)].ToString());
                if (document.PropertyBag.ContainsKey(nameof(productDoc.RegularPrice)) && document.PropertyBag[nameof(productDoc.RegularPrice)] != null)
                    productDoc.RegularPrice = double.Parse(document.PropertyBag[nameof(productDoc.RegularPrice)].ToString());
                #pragma warning restore 612, 618

                //New pricing fields
                productDoc.EntityPricing = document.PropertyBag.GetOrDeserializePropertyBagEntity<EntityPricing>(nameof(ProductDocument.EntityPricing));
                productDoc.GroupPricing = document.PropertyBag.GetOrDeserializePropertyBagEntity<GroupPricing>(nameof(ProductDocument.GroupPricing));
            }

            return productDoc;
        }

        protected override string GenerateUrl(CreateSearchPaginationParam<SearchParam> param)
        {
            var cloneParam = (SearchParam)param.SearchParameters.Clone();

            var nameValueCollection = SearchQueryUrlProvider.BuildSearchQueryString(new BuildSearchUrlParam()
            {
                SearchCriteria = cloneParam.Criteria
            });

            return nameValueCollection != null ? UrlFormatter.ToUrlString(nameValueCollection): null;
        }
    }
}