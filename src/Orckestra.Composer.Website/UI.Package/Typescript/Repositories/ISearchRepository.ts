/// <reference path='../../Typings/tsd.d.ts' />

module Orckestra.Composer {

    export interface ISearchRepository {

        /**
         * Get the facets of the current search criteria.
         */
        getFacets(searchCriteria): Q.Promise<any>;

        /**
         * Get the facets for the category page and current search criteria.
         */
        getCategoryFacets(categoryId, searchCriteria): Q.Promise<any>;

        /**
       * Get the facets for the query page and current query string.
       */
        getQueryFacets(QueryName, QueryType, QueryString): Q.Promise<any>;
    }
}