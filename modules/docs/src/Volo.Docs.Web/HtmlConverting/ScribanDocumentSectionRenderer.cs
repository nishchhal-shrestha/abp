﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Scriban;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Extensions;

namespace Volo.Docs.HtmlConverting
{
    public class ScribanDocumentSectionRenderer : IDocumentSectionRenderer
    {
        private const string JsonOpener = "````json";
        private const string JsonCloser = "````";
        private const string DocsParam = "//[doc-params]";
        private const string DocsTemplates = "//[doc-template]";
        private const string DocsNav = "//[doc-nav]";

        public ILogger<ScribanDocumentSectionRenderer> Logger { get; set; }

        public ScribanDocumentSectionRenderer()
        {
            Logger = NullLogger<ScribanDocumentSectionRenderer>.Instance;
        }

        public async Task<string> RenderAsync(string document, DocumentRenderParameters parameters = null, List<DocumentPartialTemplateContent> partialTemplates = null)
        {
            if (partialTemplates != null && partialTemplates.Any())
            {
                document = SetPartialTemplates(document, partialTemplates);
            }

            var scribanTemplate = Template.Parse(document);

            if (parameters == null)
            {
                return await scribanTemplate.RenderAsync();
            }

            var result = await scribanTemplate.RenderAsync(parameters);

            return RemoveOptionsJson(result, DocsParam, DocsNav);
        }

        public Task<Dictionary<string, List<string>>> GetAvailableParametersAsync(string document)
        {
            return GetSectionAsync<Dictionary<string, List<string>>>(document, DocsParam);
        }
        
        public Task<DocumentNavigationsDto> GetDocumentNavigationsAsync(string documentContent)
        {
            return GetSectionAsync<DocumentNavigationsDto>(documentContent, DocsNav);
        }

        protected virtual async Task<T> GetSectionAsync<T>(string document, string sectionName) where T : new()
        {
            try
            {
                if (!document.Contains(JsonOpener) || !document.Contains(sectionName))
                {
                    return new T();
                }

                var (jsonBeginningIndex, jsonEndingIndex, insideJsonSection) = GetJsonBeginEndIndexesAndPureJson(document, sectionName);

                if (jsonBeginningIndex < 0 || jsonEndingIndex <= 0 || string.IsNullOrWhiteSpace(insideJsonSection))
                {
                    return new T();
                }

                var pureJson = insideJsonSection.Replace(sectionName, "").Trim();

                if (!DocsJsonSerializerHelper.TryDeserialize<T>(pureJson, out var section))
                {
                    throw new UserFriendlyException($"ERROR-20200327: Cannot validate JSON content for `{sectionName}`!");
                }

                return await Task.FromResult(section);
            }
            catch (Exception)
            {
                Logger.LogWarning("Unable to parse parameters of document.");
                return new T();
            }
        }

        private static string RemoveOptionsJson(string document, params string[] sectionNames)
        {

            foreach (var sectionName in sectionNames)
            {
                var orgDocument = document;

                try
                {
                    if (!document.Contains(JsonOpener) || !document.Contains(sectionName))
                    {
                        continue;
                    }

                    var (jsonBeginningIndex, jsonEndingIndex, insideJsonSection) = GetJsonBeginEndIndexesAndPureJson(document, sectionName);

                    if (jsonBeginningIndex < 0 || jsonEndingIndex <= 0 || string.IsNullOrWhiteSpace(insideJsonSection))
                    {
                        continue;
                    }

                    document = document.Remove(
                        jsonBeginningIndex - JsonOpener.Length,
                        (jsonEndingIndex + JsonCloser.Length) - (jsonBeginningIndex - JsonOpener.Length)
                    );
                }
                catch (Exception)
                {
                    document = orgDocument;
                }
            }
            
            return document;
        }

        private static (int, int, string) GetJsonBeginEndIndexesAndPureJson(string document, string sectionName)
        {
            var searchedIndex = 0;

            while (searchedIndex < document.Length)
            {
                var jsonBeginningIndex = document.Substring(searchedIndex).IndexOf(JsonOpener, StringComparison.Ordinal) + JsonOpener.Length + searchedIndex;

                if (jsonBeginningIndex < 0)
                {
                    return (-1, -1, "");
                }

                var jsonEndingIndex = document.Substring(jsonBeginningIndex).IndexOf(JsonCloser, StringComparison.Ordinal) + jsonBeginningIndex;
                var insideJsonSection = document[jsonBeginningIndex..jsonEndingIndex];

                if (insideJsonSection.IndexOf(sectionName, StringComparison.Ordinal) < 0)
                {
                    searchedIndex = jsonEndingIndex + JsonCloser.Length;
                    continue;
                }

                return (jsonBeginningIndex, jsonEndingIndex, insideJsonSection);
            }

            return (-1, -1, "");
        }

        public async Task<List<DocumentPartialTemplateWithValuesDto>> GetPartialTemplatesInDocumentAsync(string documentContent)
        {
            var templates = new List<DocumentPartialTemplateWithValuesDto>();

            while (documentContent.Contains(JsonOpener))
            {
                var afterJsonOpener = documentContent.Substring(
                    documentContent.IndexOf(JsonOpener, StringComparison.Ordinal) + JsonOpener.Length
                );

                var betweenJsonOpenerAndCloser = afterJsonOpener.Substring(0,
                    afterJsonOpener.IndexOf(JsonCloser, StringComparison.Ordinal)
                );

                documentContent = afterJsonOpener.Substring(
                    afterJsonOpener.IndexOf(JsonCloser, StringComparison.Ordinal) + JsonCloser.Length
                );

                if (!betweenJsonOpenerAndCloser.Contains(DocsTemplates))
                {
                    continue;
                }

                var json = betweenJsonOpenerAndCloser.Substring(betweenJsonOpenerAndCloser.IndexOf(DocsTemplates, StringComparison.Ordinal) + DocsTemplates.Length);

                if (!DocsJsonSerializerHelper.TryDeserialize<DocumentPartialTemplateWithValuesDto>(json, out var template))
                {
                    throw new UserFriendlyException($"ERROR-20200327: Cannot validate JSON content for `AvailableParameters`!");
                }

                templates.Add(template);
            }

            return await Task.FromResult(templates);
        }

        private static string SetPartialTemplates(string document, IReadOnlyCollection<DocumentPartialTemplateContent> templates)
        {
            var newDocument = new StringBuilder();

            while (document.Contains(JsonOpener))
            {
                var beforeJson = document.Substring(0,
                    document.IndexOf(JsonOpener, StringComparison.Ordinal) + JsonOpener.Length
                );

                var afterJsonOpener = document.Substring(
                    document.IndexOf(JsonOpener, StringComparison.Ordinal) + JsonOpener.Length
                );

                var betweenJsonOpenerAndCloser = afterJsonOpener.Substring(0,
                    afterJsonOpener.IndexOf(JsonCloser, StringComparison.Ordinal)
                );

                if (!betweenJsonOpenerAndCloser.Contains(DocsTemplates))
                {
                    document = afterJsonOpener.Substring(
                        afterJsonOpener.IndexOf(JsonCloser, StringComparison.Ordinal) + JsonCloser.Length
                    );

                    newDocument.Append(beforeJson + betweenJsonOpenerAndCloser + JsonCloser);
                    continue;
                }

                var json = betweenJsonOpenerAndCloser.Substring(
                    betweenJsonOpenerAndCloser.IndexOf(DocsTemplates, StringComparison.Ordinal) + DocsTemplates.Length
                );

                if (DocsJsonSerializerHelper.TryDeserialize<DocumentPartialTemplateWithValuesDto>(json, out var documentPartialTemplateWithValuesDto))
                {
                    var template = templates.FirstOrDefault(t => t.Path == documentPartialTemplateWithValuesDto.Path);

                    var beforeTemplate = document.Substring(0,
                        document.IndexOf(JsonOpener, StringComparison.Ordinal)
                    );

                    newDocument.Append(beforeTemplate + template?.Content + JsonCloser);

                    document = afterJsonOpener.Substring(
                        afterJsonOpener.IndexOf(JsonCloser, StringComparison.Ordinal) + JsonCloser.Length
                    );
                }
            }

            newDocument.Append(document);

            return newDocument.ToString();
        }
    }
}
