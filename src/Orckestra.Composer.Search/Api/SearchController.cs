﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Orckestra.Composer.Providers;
using Orckestra.Composer.Search.Parameters;
using Orckestra.Composer.Search.Providers;
using Orckestra.Composer.Search.Request;
using Orckestra.Composer.Search.Services;
using Orckestra.Composer.Search.ViewModels;
using Orckestra.Composer.Services;
using Orckestra.Composer.Utils;
using Orckestra.Composer.WebAPIFilters;
using Orckestra.Overture.ServiceModel.Products;
using Orckestra.Overture.ServiceModel.Search;

namespace Orckestra.Composer.Search.Api
{
    [ValidateLanguage]
    [JQueryOnlyFilter]
    public class SearchController : ApiController
    {
        private const int MAXIMUM_AUTOCOMPLETE_RESULT = 8;
        private const int MAXIMUM_SEARCH_TERMS_SUGGESTIONS = 5;
        private const int MAXIMUM_CATEGORIES_SUGGESTIONS = 4;
        private const int MAXIMUM_BRAND_SUGGESTIONS = 3;

        protected IComposerContext ComposerContext { get; private set; }
        protected ISearchViewService SearchViewService { get; private set; }
        protected ICategoryBrowsingViewService CategoryBrowsingViewService { get; private set; }
        protected IInventoryLocationProvider InventoryLocationProvider { get; private set; }
        protected ISearchTermsTransformationProvider SearchTermsTransformationProvider { get; private set; }
        protected IAutocompleteProvider AutocompleteProvider { get; private set; }
        protected ISearchUrlProvider SearchUrlProvider { get; set; }
        protected IBaseSearchCriteriaProvider BaseSearchCriteriaProvider { get; private set; }
        protected ICategoryBrowsingUrlProvider CategoryBrowsingUrlProvider { get; }

        public SearchController(
            IComposerContext composerContext,
            ISearchViewService searchViewService,
            IInventoryLocationProvider inventoryLocationProvider,
            ISearchTermsTransformationProvider searchTermsTransformationProvider,
            IAutocompleteProvider autocompleteProvider,
            ISearchUrlProvider searchUrlProvider,
            ICategoryBrowsingViewService categoryBrowsingViewService,
            IBaseSearchCriteriaProvider baseSearchCriteriaProvider,
            ICategoryBrowsingUrlProvider categoryBrowsingUrlProvider)
        {
            ComposerContext = composerContext ?? throw new ArgumentNullException(nameof(composerContext));
            SearchViewService = searchViewService ?? throw new ArgumentNullException(nameof(searchViewService));
            CategoryBrowsingViewService = categoryBrowsingViewService ?? throw new ArgumentNullException(nameof(categoryBrowsingViewService));
            InventoryLocationProvider = inventoryLocationProvider ?? throw new ArgumentNullException(nameof(inventoryLocationProvider));
            SearchTermsTransformationProvider = searchTermsTransformationProvider ?? throw new ArgumentNullException(nameof(searchTermsTransformationProvider));
            AutocompleteProvider = autocompleteProvider ?? throw new ArgumentNullException(nameof(autocompleteProvider));
            SearchUrlProvider = searchUrlProvider ?? throw new ArgumentNullException(nameof(searchUrlProvider));
            BaseSearchCriteriaProvider = baseSearchCriteriaProvider ?? throw new ArgumentNullException(nameof(baseSearchCriteriaProvider));
            CategoryBrowsingUrlProvider = categoryBrowsingUrlProvider ?? throw new ArgumentNullException(nameof(categoryBrowsingUrlProvider));
        }


        [ActionName("getfacets")]
        [HttpPost]
        [ValidateModelState]
        public virtual async Task<IHttpActionResult> GetFacets(GetFacetsRequest request)
        {
            var queryString = HttpUtility.ParseQueryString(request.QueryString);

            var searchCriteria = await BaseSearchCriteriaProvider.GetSearchCriteriaAsync(queryString["keywords"], RequestUtils.GetBaseUrl(Request).ToString(), true).ConfigureAwait(false);
            searchCriteria.NumberOfItemsPerPage = 0;

            searchCriteria.SelectedFacets.AddRange(SearchUrlProvider.BuildSelectedFacets(queryString));

            var searchResultsViewModel = await SearchViewService.GetSearchViewModelAsync(searchCriteria).ConfigureAwait(false);
            searchResultsViewModel.ProductSearchResults.Facets = searchResultsViewModel.ProductSearchResults.Facets.Where(f => !f.FieldName.StartsWith(SearchConfiguration.CategoryFacetFiledNamePrefix)).ToList();

            return Ok(searchResultsViewModel);
        }

        [ActionName("getcategoryfacets")]
        [HttpPost]
        [ValidateModelState]
        public virtual async Task<IHttpActionResult> GetCategoryFacets(GetCategoryFacetsRequest request)
        {
            var param = new GetCategoryBrowsingViewModelParam
            {
                CategoryId = request.CategoryId,
                CategoryName = string.Empty,
                BaseUrl = RequestUtils.GetBaseUrl(Request).ToString(),
                IsAllProducts = false,
                NumberOfItemsPerPage = 0,
                Page = 1,
                SortBy = "score",
                SortDirection = "desc",
                InventoryLocationIds = await InventoryLocationProvider.GetInventoryLocationIdsForSearchAsync().ConfigureAwait(false),
                CultureInfo = ComposerContext.CultureInfo
            };

            if (!string.IsNullOrEmpty(request.QueryString))
            {
                var queryString = HttpUtility.ParseQueryString(request.QueryString);
                param.SelectedFacets = SearchUrlProvider.BuildSelectedFacets(queryString).ToList();
            }

            var viewModel = await CategoryBrowsingViewService.GetCategoryBrowsingViewModelAsync(param).ConfigureAwait(false);
            viewModel.ProductSearchResults.Facets = viewModel.ProductSearchResults.Facets.Where(f => !f.FieldName.StartsWith(SearchConfiguration.CategoryFacetFiledNamePrefix)).ToList();

            return Ok(viewModel);
        }

        [ActionName("autocomplete")]
        [HttpPost]
        public virtual async Task<IHttpActionResult> AutoComplete(AutoCompleteSearchViewModel request, int limit = MAXIMUM_AUTOCOMPLETE_RESULT)
        {
            var originalSearchTerms = request.Query.Trim();
            var searchTerms = SearchTermsTransformationProvider.TransformSearchTerm(originalSearchTerms, ComposerContext.CultureInfo.Name);

            var searchCriteria = await BaseSearchCriteriaProvider.GetSearchCriteriaAsync(searchTerms, RequestUtils.GetBaseUrl(Request).ToString(), false).ConfigureAwait(false);
            searchCriteria.NumberOfItemsPerPage = limit;

            var searchResultsViewModel = await SearchViewService.GetSearchViewModelAsync(searchCriteria).ConfigureAwait(false);
            if (searchResultsViewModel.ProductSearchResults?.TotalCount == 0 && originalSearchTerms != searchTerms)
            {
                searchCriteria.Keywords = originalSearchTerms;
                searchResultsViewModel = await SearchViewService.GetSearchViewModelAsync(searchCriteria).ConfigureAwait(false);
            }

            var vm = new AutoCompleteViewModel() { Suggestions = new List<ProductSearchViewModel>() };
            if (searchResultsViewModel.ProductSearchResults?.SearchResults?.Count > 0)
            {
                vm.Suggestions = searchResultsViewModel.ProductSearchResults.SearchResults.Take(limit)
                    .Select(p => { p.SearchTerm = searchTerms; return p; })
                    .ToList();
            }

            return Ok(vm);
        }

        private static string RootCategoryId = "Root";
        private static Regex CategoryFieldName = new Regex(@"CategoryLevel(\d+)_Facet");

        [ActionName("suggestCategories")]
        [HttpPost]
        public virtual async Task<IHttpActionResult> SuggestCategories(AutoCompleteSearchViewModel request, int limit = MAXIMUM_CATEGORIES_SUGGESTIONS, bool withCategoriesUrl = false)
        {
            string language = ComposerContext.CultureInfo.Name;
            string searchTerm = request.Query.Trim().ToLower();

            List<Category> categories = await SearchViewService.GetAllCategories();
            List<Facet> categoryCounts = await SearchViewService.GetCategoryProductCounts(language);

            List<CategorySuggestionViewModel> categorySuggestionList = new List<CategorySuggestionViewModel>();

            foreach (var category in categories)
            {
                if (!category.DisplayName.TryGetValue(language, out string displayName)) continue;

                // Find the parents of the category
                List<Category> parents = new List<Category>();
                Category currentNode = category;
                while (!string.IsNullOrWhiteSpace(currentNode.PrimaryParentCategoryId) && currentNode.PrimaryParentCategoryId != RootCategoryId)
                {
                    Category parent = categories.Single((cat) => cat.Id == currentNode.PrimaryParentCategoryId);
                    parents.Add(parent);
                    currentNode = parent;
                }
                parents.Reverse();

                FacetValue categoryCount = categoryCounts
                        .Where((facet) => int.TryParse(CategoryFieldName.Match(facet.FieldName).Groups[1].Value, out int n) && parents.Count == n - 1)
                        .FirstOrDefault()?
                        .Values
                        .Where((facetValue) => facetValue.Value == category.DisplayName[language]).SingleOrDefault();

                if (categoryCount != null)
                {
                    categorySuggestionList.Add(new CategorySuggestionViewModel
                    {
                        DisplayName = displayName,
                        Parents = parents.Select((parent) => parent.DisplayName[language]).ToList(),
                        Quantity = categoryCount.Count,
                        Id = category.Id
                    });
                }
            };

            List<CategorySuggestionViewModel> finalSuggestions = categorySuggestionList
                .Where((suggestion) => suggestion.DisplayName.ToLower().Contains(searchTerm))
                .OrderByDescending((category) => category.Quantity)
                .Take(limit)
                .ToList();

            foreach (var suggestion in finalSuggestions)
            {
                List<CategorySuggestionViewModel> parents = new List<CategorySuggestionViewModel>();
                foreach (var parent in suggestion.Parents)
                {
                    var parentInfo = categorySuggestionList.Find(x => x.DisplayName == parent);
                    parents.Add(parentInfo);
                }
                suggestion.ParentsFullInfo = parents;
            }

            if (withCategoriesUrl)
            {
                foreach (var category in finalSuggestions)
                {
                    category.Url = GetCategoryUrl(category.Id);

                    foreach (var parent in category.ParentsFullInfo)
                    {
                        parent.Url = GetCategoryUrl(parent.Id);
                    }
                }
            }

            CategorySuggestionsViewModel vm = new CategorySuggestionsViewModel
            {
                Suggestions = finalSuggestions
            };

            return Ok(vm);
        }

        private string GetCategoryUrl(string id)
        {
            return CategoryBrowsingUrlProvider.BuildCategoryBrowsingUrl(new BuildCategoryBrowsingUrlParam
            {
                CategoryId = id,
                BaseUrl = RequestUtils.GetBaseUrl(Request).ToString(),
                CultureInfo = ComposerContext.CultureInfo,
                IsAllProductsPage = false
            });
        }

        [ActionName("suggestBrands")]
        [HttpPost]
        public virtual async Task<IHttpActionResult> SuggestBrands(AutoCompleteSearchViewModel request, int limit = MAXIMUM_BRAND_SUGGESTIONS)
        {
            string searchTerm = request.Query.Trim().ToLower();

            List<Facet> facets = await SearchViewService.GetBrandProductCounts(ComposerContext.CultureInfo.Name).ConfigureAwait(false);
            List<BrandSuggestionViewModel> brandList = facets.Single().Values.Select(facetValue => new BrandSuggestionViewModel
            {
                DisplayName = facetValue.DisplayName
            }).ToList();

            BrandSuggestionsViewModel vm = new BrandSuggestionsViewModel()
            {
                Suggestions = brandList
                    .Where((suggestion) => suggestion.DisplayName.ToLower().Contains(searchTerm))
                    .OrderBy(x => x.DisplayName)
                    .Take(limit).ToList()
            };
            return Ok(vm);
        }

        [ActionName("suggestTerms")]
        [HttpPost]
        public virtual async Task<IHttpActionResult> SuggestTerms(AutoCompleteSearchViewModel request, int limit = MAXIMUM_SEARCH_TERMS_SUGGESTIONS)
        {
            string searchTerm = request.Query.Trim().ToLower();

            List<string> suggestedTerms = await AutocompleteProvider.GetSearchSuggestedTerms(ComposerContext.CultureInfo, searchTerm).ConfigureAwait(false);

            SearchTermsSuggestionsViewModel vm = new SearchTermsSuggestionsViewModel()
            {
                Suggestions = suggestedTerms
                    .OrderBy(term => term)
                    .Select(term => new SearchTermsSuggestionViewModel { DisplayName = term })
                    .Take(limit).ToList()
            };
            return Ok(vm);
        }
    }
}