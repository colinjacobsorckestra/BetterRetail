﻿using System.Collections.Generic;
using Orckestra.Composer.Search.Facets;
using Orckestra.Composer.ViewModels;

namespace Orckestra.Composer.Search.ViewModels
{
    public sealed class CategoryBrowsingViewModel : BaseViewModel
    {
        public string CategoryId { get; set; }

        public string CategoryName { get; set; }

        public ProductSearchResultsViewModel ProductSearchResults { get; set; }

        public FacetSettingsViewModel FacetSettings { get; set; }

        public List<string> LandingPageUrls { get; set; }
    }
}
