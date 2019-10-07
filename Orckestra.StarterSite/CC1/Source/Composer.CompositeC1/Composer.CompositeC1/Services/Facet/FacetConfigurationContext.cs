﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Composite.Data.Types;
using Orckestra.Composer.CompositeC1.DataTypes.Facets;
using Orckestra.Composer.CompositeC1.Mappers;
using Orckestra.Composer.CompositeC1.Services.DataQuery;
using Orckestra.Composer.Search;
using Orckestra.Composer.Search.Context;

namespace Orckestra.Composer.CompositeC1.Services.Facet
{
    public class FacetConfigurationContext : IFacetConfigurationContext
    {
        protected IFacetConfigurationCache Cache{ get; }
        protected HttpContextBase HttpContext { get; }
        protected IDataQueryService DataQueryService { get; }
        private List<FacetSetting> _facetSettings;

        public FacetConfigurationContext(HttpContextBase httpContext, IDataQueryService dataQueryService, IFacetConfigurationCache facetCacheService)
        {
            Cache = facetCacheService;
            HttpContext = httpContext;
            DataQueryService = dataQueryService;
        }

        public virtual List<FacetSetting> GetFacetSettings()
        {
            if (_facetSettings == null)
            {
                var pageId = GetPageId();
                _facetSettings = Cache.GetOrAdd(pageId, _ => LoadFacetSettings(pageId));
            }
            
            return _facetSettings;
        }


        protected virtual List<FacetSetting> LoadFacetSettings(Guid pageId)
        {
            var result = new List<FacetSetting>();

            var facetConfiguration = GetFacetConfigurationFromPage(pageId);

            if (facetConfiguration == null) // if no config on page, using any config from db
            {
                facetConfiguration = GetDefaultFacetConfiguration();

                if (facetConfiguration == null) // no facet configs exist
                    return result;
            }

            var facetsIds = GetIds(facetConfiguration.Facets);

            var facets = DataQueryService.Get<IFacet>().Where(f => facetsIds.Contains(f.Id)).ToList();

            var facetsAndData = facets.Select(f => new
            {
                Facet = f,
                DependsOnIds = GetIds(f.DependsOn),
                PromotedIds = GetIds(f.PromotedValues),
            }).ToList();

            var allDependsOnIds = facetsAndData.SelectMany(f => f.DependsOnIds).Distinct().ToList();
            var allDependsOn = LoadDependsOn(allDependsOnIds, facets);

            var allPromotedIds = facetsAndData.SelectMany(f => f.PromotedIds).Distinct().ToList();
            var allPromoted = LoadPromotedFacetValues(allPromotedIds);

            foreach (var facet in facetsAndData)
            {
                var dependsOn = facet.DependsOnIds
                    .Select(id => allDependsOn.TryGetValue(id, out var dependedFacet) ? dependedFacet : null)
                    .Where(f => f != null)
                    .ToList();

                var promoted = facet.PromotedIds
                    .Select(id => allPromoted.TryGetValue(id, out var promotedFacet) ? promotedFacet : null)
                    .Where(f => f != null)
                    .ToList();

                result.Add(FacetsMapper.ConvertToFacetSetting(facet.Facet, dependsOn, promoted));
            }

            return result;
        }

        protected IFacetConfiguration GetFacetConfigurationFromPage(Guid pageId)
        {
            if (pageId == Guid.Empty)
                return null;

            var metaQuery = DataQueryService.Get<IFacetConfigurationMeta>();
            var configQuery = DataQueryService.Get<IFacetConfiguration>();

            var join = from m in metaQuery
                       join c in configQuery on m.Configuration equals c.Id
                       where m.PageId == pageId
                       select c;

            return join.FirstOrDefault();
        }

        protected virtual IFacetConfiguration GetDefaultFacetConfiguration()
        {
            return DataQueryService.Get<IFacetConfiguration>()
                .OrderByDescending(c => c.IsDefault)
                .FirstOrDefault();
        }

        protected Guid GetPageId()
        {
            const string pageIdKey = "PageRenderer.IPage";
            if (HttpContext == null || !HttpContext.Items.Contains(pageIdKey))
                return Guid.Empty;

            var page = HttpContext.Items[pageIdKey] as IPage;
            if (page == null)
                return Guid.Empty;

            return page.Id;
        }

        private Dictionary<Guid, IFacet> LoadDependsOn(List<Guid> dependsOnIds, List<IFacet> existingFacets)
        {
            var result = new Dictionary<Guid, IFacet>();
            for (int i = dependsOnIds.Count - 1; i >= 0; i--)
            {
                var facetId = dependsOnIds[i];
                var facet = existingFacets.FirstOrDefault(f => f.Id == facetId);
                if (facet != null)
                    result[facetId] = facet;
                dependsOnIds.RemoveAt(i);
            }

            if (dependsOnIds.Count > 0)
            {
                var dependsOn = DataQueryService.Get<IFacet>().Where(f => dependsOnIds.Contains(f.Id)).ToList();
                foreach (var facet in dependsOn)
                {
                    result[facet.Id] = facet;
                }
            }

            return result;
        }

        private Dictionary<Guid, IPromotedFacetValueSetting> LoadPromotedFacetValues(List<Guid> allPromotedIds)
        {
            if (allPromotedIds.Count == 0)
                return new Dictionary<Guid, IPromotedFacetValueSetting>();

            return DataQueryService.Get<IPromotedFacetValueSetting>().Where(f => allPromotedIds.Contains(f.Id)).ToDictionary(f => f.Id, f => f);
        }

        protected static List<Guid> GetIds(string idString)
        {
            return idString.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Guid.Parse)
                .ToList();
        }
    };
}
