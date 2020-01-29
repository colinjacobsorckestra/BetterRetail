(function() {
    'use strict';

    module.exports = function() {
        var mainConfiguration = require('./common/config'),
            _ = require('lodash'),
            path = require('path'),
            config = {};
            let thirdPartyJavaScriptPath = '../../../Composer.CompositeC1/Composer.CompositeC1.Mvc/UI.Package/JavaScript/';

        config = _.merge(mainConfiguration, {

            typescriptFilesGlob: ['./*.UI/*/Source/Typescript/**/*.ts', './*.UI/Source/Typescript/**/*.ts'],
            typescriptFilesGlobForUnitTests: ['./*.UI/*/Source/Typescript/**/*.ts', './*.UI/Source/Typescript/**/*.ts', '!./Composer.UI/Source/Typescript/App.ts'],
            dtsOutputFolder: './UI.Package/Typings',
            javascriptFolder: './UI.Package/JavaScript',
            jsBundleName: 'orckestra.composer.js',
            dtsBundleName: 'orckestra.composer.d.ts',

            karma: {
                files: [
                    '../../3rdParty.ForTests/**/*.js',
                    path.join(thirdPartyJavaScriptPath, 'jquery-1.11.2.min.js'),
                    path.join(thirdPartyJavaScriptPath, 'lodash.min.js'),
                    path.join(thirdPartyJavaScriptPath, 'parsley.min.js'),
                    path.join(thirdPartyJavaScriptPath, 'handlebars.helpers.js'),
                    path.join(thirdPartyJavaScriptPath, 'q-1.2.0.js'),
                    path.join(thirdPartyJavaScriptPath, 'jquery.serialize-object.js'),
                    path.join(thirdPartyJavaScriptPath, 'typeahead.js'),
                    '../../UI.Package/JavaScript/orckestra.composer.js',
                    path.join('../../', mainConfiguration.testsOutputFolder, '/**/*.js')
                ],
                filesToBuild: [
                    'Composer.UI/Source/Tests/unit/**/*.ts',
                    'Composer.*.UI/*/Source/Tests/unit/**/*.ts'
                ]
            },

            tokens: ['Product', 'Cart', 'MyAccount'],

            documentationSettings: {
                documentationName: 'Orckestra Composer',
                outputFolder: './UI.Package/Documentation',
                moduleType: 'commonjs',
                includeDeclarations: true
            },

            watch: {
                delays: {
                    composerAssemblies: 1000,
                },
            },

            composerAssemblies: ['./Packaging/bin/Debug/*.dll', '!./Packaging/bin/Debug/Packaging.dll'],
			composerc1Assemplies: [''],
			deployedWebsitePath: '../../deployment/WebSite'
        });

        return config;
    }();
})();
