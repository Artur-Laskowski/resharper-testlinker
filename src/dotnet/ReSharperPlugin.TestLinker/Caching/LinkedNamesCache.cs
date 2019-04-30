﻿// Copyright Matthias Koch 2017.
// Distributed under the MIT License.
// https://github.com/matkoch/Nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application.Progress;
using JetBrains.DataFlow;
using JetBrains.Diagnostics;
using JetBrains.Lifetimes;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;

namespace TestLinker.Caching
{
    [PsiComponent]
    public class LinkedNamesCache : SimpleICache<LinkedNamesData>
    {
        private readonly IReadOnlyList<ILinkedTypesProvider> _linkedTypesProviders;

        private LinkedNamesMergeData _mergeData;

        public LinkedNamesCache (
            Lifetime lifetime,
            IPersistentIndexManager persistentIndexManager,
            IEnumerable<ILinkedTypesProvider> linkedTypesProviders)
            : base(lifetime, persistentIndexManager, new LinkedNamesDataMarshaller())
        {
            _linkedTypesProviders = linkedTypesProviders.ToList();
        }

        public override string Version => "7";

        public OneToSetMap<string, Pair<IPsiSourceFile, string>> LinkedNamesMap => _mergeData.LinkedNamesMap;

        public override object Load ([NotNull] IProgressIndicator progress, bool enablePersistence)
        {
            var data = new LinkedNamesMergeData();
            foreach (var x in Map.Where(x => x.Key != null))
                LoadFile(data, x.Key, x.Value);
            return data;
        }

        public override void MergeLoaded ([NotNull] object data)
        {
            _mergeData = (LinkedNamesMergeData) data;

            base.MergeLoaded(data);
        }

        [NotNull]
        public override object Build (IPsiSourceFile sourceFile, bool isStartup)
        {
            return GetLinkData(sourceFile);
        }

        public override void Merge (IPsiSourceFile sourceFile, [CanBeNull] object builtPart)
        {
            var linkedNamesData = (LinkedNamesData) builtPart;
            if (linkedNamesData == null)
                return;

            base.Merge(sourceFile, linkedNamesData);

            RemoveData(sourceFile);

            LoadFile(_mergeData, sourceFile, linkedNamesData);
        }

        public override void Drop (IPsiSourceFile sourceFile)
        {
            RemoveData(sourceFile);

            base.Drop(sourceFile);
        }

        protected override bool IsApplicable ([NotNull] IPsiSourceFile sourceFile)
        {
            return sourceFile.GetDominantPsiFile<CSharpLanguage>() != null;
        }

        #region Privates

        private void LoadFile (LinkedNamesMergeData data, IPsiSourceFile sourceFile, LinkedNamesData linkedNamesData)
        {
            var sourceNames = linkedNamesData.Keys;
            data.PreviousNamesMap.AddRange(sourceFile, sourceNames);
            foreach (var sourceName in sourceNames)
            {
                var linkedNames = linkedNamesData[sourceName];
                data.PreviousNamesMap.AddRange(sourceFile, linkedNames);

                foreach (var linkedName in linkedNames)
                {
                    data.LinkedNamesMap.Add(sourceName, Pair.Of(sourceFile, linkedName));
                    data.LinkedNamesMap.Add(linkedName, Pair.Of(sourceFile, sourceName));
                }
            }
        }

        private void RemoveData (IPsiSourceFile sourceFile)
        {
            var previousNames = _mergeData.PreviousNamesMap[sourceFile];
            foreach (var previousName in previousNames)
            foreach (var link in _mergeData.LinkedNamesMap[previousName].Where(x => x.First == sourceFile).ToList())
                _mergeData.LinkedNamesMap.Remove(previousName, link);

            _mergeData.PreviousNamesMap.RemoveKey(sourceFile);
        }

        private LinkedNamesData GetLinkData (IPsiSourceFile sourceFile)
        {
            var linkedNamesData = new LinkedNamesData();
            foreach (var sourceType in GetTypeDeclarations(sourceFile.GetPrimaryPsiFile().NotNull()))
            {
                for (var i = 0; i < _linkedTypesProviders.Count; i++)
                {
                    var linkedNames = _linkedTypesProviders[i].GetLinkedNames(sourceType);
                    foreach (var x in linkedNames)
                        linkedNamesData.Add(sourceType.DeclaredName, x);
                }
            }

            return linkedNamesData;
        }

        private IEnumerable<ITypeDeclaration> GetTypeDeclarations (ITreeNode node)
        {
            var namespaceDeclarationHolder = node as INamespaceDeclarationHolder;
            if (namespaceDeclarationHolder != null)
                foreach (var typeDeclaration in namespaceDeclarationHolder.NamespaceDeclarations.SelectMany(GetTypeDeclarations))
                    yield return typeDeclaration;

            var typeDeclarationHolder = node as ITypeDeclarationHolder;
            if (typeDeclarationHolder != null)
                foreach (var typeDeclaration in typeDeclarationHolder.TypeDeclarations)
                    yield return typeDeclaration;
        }

        #endregion
    }
}
