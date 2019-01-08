﻿using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Summary;
using Hl7.Fhir.Utility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Hl7.Fhir.Specification.Tests
{
    [TestClass]
    public class ArtifactSummaryTests
    {
        // [WMR 20181213] ModelInfo.Version returns 3.6 ...?
        static readonly string ApiFhirVersion = "4.0.0"; // ModelInfo.Version;

        [TestMethod]
        public void TestPatientXmlSummary() => TestPatientSummary(@"TestData\TestPatient.xml");

        [TestMethod]
        public void TestPatientJsonSummary() => TestPatientSummary(@"TestData\TestPatient.json");

        void TestPatientSummary(string path)
        {
            var summary = assertSummary(path);
            Assert.AreEqual(ResourceType.Patient.GetLiteral(), summary.ResourceTypeName);
        }

        [TestMethod]
        public void TestPatientXmlSummaryWithCustomHarvester()
            => TestPatientSummaryWithCustomHarvester(@"TestData\TestPatient.xml", "Donald");

        [TestMethod]
        public void TestPatientJsonSummaryWithCustomHarvester()
            => TestPatientSummaryWithCustomHarvester(@"TestData\TestPatient.json", "Chalmers");

        void TestPatientSummaryWithCustomHarvester(string path, params string[] expectedNames)
        {
            // Combine default harvesters and custom harvester
            var harvesters = new ArtifactSummaryHarvester[ArtifactSummaryGenerator.ConformanceHarvesters.Length + 1];
            Array.Copy(ArtifactSummaryGenerator.ConformanceHarvesters, harvesters, ArtifactSummaryGenerator.ConformanceHarvesters.Length);
            harvesters[ArtifactSummaryGenerator.ConformanceHarvesters.Length] = HarvestPatientSummary;

            var summary = assertSummary(path, harvesters);
            Assert.AreEqual(ResourceType.Patient.GetLiteral(), summary.ResourceTypeName);
            var familyNames = summary.GetValueOrDefault<IReadOnlyList<string>>(PatientFamilyNameKey);
            Assert.IsNotNull(familyNames);
            Assert.AreEqual(1, familyNames.Count);
            Assert.IsTrue(expectedNames.SequenceEqual(familyNames));
        }

        // Custom artifact summary harvester implementation to extract family name(s) from Patient resources
        const string PatientFamilyNameKey = "Patient.name.family";
        static bool HarvestPatientSummary(ISourceNode nav, ArtifactSummaryPropertyBag properties)
        {
            if (properties.GetTypeName() == ResourceType.Patient.GetLiteral())
            {
                nav.HarvestValues(properties, PatientFamilyNameKey, "name", "family");
                return true;
            }
            return false;
        }

        [TestMethod]
        public void TestValueSetXmlSummary()
        {
            const string path = @"TestData\validation\SectionTitles.valueset.xml";
            const string url = @"http://example.org/ValueSet/SectionTitles";
            var summary = assertSummary(path);

            // Common properties
            Assert.AreEqual(ResourceType.ValueSet.GetLiteral(), summary.ResourceTypeName);
            Assert.IsTrue(summary.ResourceType == ResourceType.ValueSet);

            // Conformance resource properties
            Assert.IsNotNull(summary.GetConformanceCanonicalUrl());
            Assert.AreEqual(url, summary.GetConformanceCanonicalUrl());
            Assert.AreEqual("MainBundle Section title codes", summary.GetConformanceName());
            Assert.AreEqual(PublicationStatus.Draft.GetLiteral(), summary.GetPublicationStatus());
        }


        [TestMethod]
        public void TestExtensionDefinitionSummary()
        {
            const string path = @"TestData\snapshot-test\extensions\extension-patient-religion.xml";
            const string url = @"http://hl7.org/fhir/StructureDefinition/patient-religion";
            var summary = assertSummary(path);
            // Common properties
            Assert.AreEqual(ResourceType.StructureDefinition.GetLiteral(), summary.ResourceTypeName);
            Assert.IsTrue(summary.ResourceType == ResourceType.StructureDefinition);
            // Conformance resource properties
            Assert.IsNotNull(summary.GetConformanceCanonicalUrl());
            Assert.AreEqual(url, summary.GetConformanceCanonicalUrl());
            Assert.AreEqual("religion", summary.GetConformanceName());
            Assert.AreEqual(PublicationStatus.Draft.GetLiteral(), summary.GetPublicationStatus());
            // StructureDefinition properties

            //[WMR 20181218] STU3 OBSOLETE
            //var context = summary.GetStructureDefinitionContext();
            //Assert.IsNotNull(context);
            //Assert.AreEqual(1, context.Length);
            //Assert.AreEqual("Patient", context[0]);

            //[WMR 20181218] R4 NEW
            var contexts = summary.GetStructureDefinitionContext();
            Assert.IsNotNull(contexts);
            var context = contexts.FirstOrDefault();
            Assert.AreEqual(StructureDefinition.ExtensionContextType.Fhirpath.GetLiteral(), context.Type); // "fhirpath"
            Assert.AreEqual(ModelInfo.ResourceTypeToFhirTypeName(ResourceType.Patient), context.Expression); // "Patient"
        }


        [TestMethod]
        public void TestProfilesTypesJson()
        {
            const string path = @"TestData\profiles-types.json";

            var summaries = ArtifactSummaryGenerator.Default.Generate(path);
            Assert.IsNotNull(summaries);
            Assert.AreNotEqual(0, summaries.Count);
            for (int i = 0; i < summaries.Count; i++)
            {
                var summary = summaries[i];
                Assert.IsFalse(summary.IsFaulted);

                // Common properties
                Assert.AreEqual(path, summary.Origin);

                var fi = new FileInfo(path);
                Assert.AreEqual(fi.Length, summary.FileSize);
                Assert.AreEqual(fi.LastWriteTimeUtc, summary.LastModified);

                Assert.AreEqual(ResourceType.StructureDefinition.GetLiteral(), summary.ResourceTypeName);
                Assert.IsTrue(summary.ResourceType == ResourceType.StructureDefinition);

                // Conformance resource properties
                Assert.IsNotNull(summary.GetConformanceCanonicalUrl());
                Assert.IsTrue(summary.GetConformanceCanonicalUrl().ToString().StartsWith("http://hl7.org/fhir/StructureDefinition/"));
                var name = summary.GetConformanceName();
                Assert.IsNotNull(name);
                Assert.IsNotNull(summary.GetPublicationStatus());

                // [WMR 20181213] R4 NEW - Also harvest core extensions:
                var standardsStatus = summary.GetStructureDefinitionStandardsStatus();
                Assert.IsNotNull(standardsStatus);
                var isNormative = standardsStatus == "normative";
                var normativeVersion = summary.GetStructureDefinitionNormativeVersion();
                if (isNormative)
                {
                    Assert.IsNotNull(normativeVersion);
                }
                var expectedStatus =
                    isNormative
                    // [WMR 20181213] R4 HACK - These 2 datatypes still define status="draft" ?
                    && name != "MoneyQuantity"
                    && name != "SimpleQuantity"
                    ? PublicationStatus.Active : PublicationStatus.Draft;
                Assert.AreEqual(expectedStatus.GetLiteral(), summary.GetPublicationStatus());

                //Debug.WriteLine($"{summary.ResourceType} | {summary.Canonical()} | {summary.Name()}");

                // StructureDefinition properties

                Assert.IsNotNull(summary.GetStructureDefinitionFhirVersion());

                Assert.AreEqual(ApiFhirVersion, summary.GetStructureDefinitionFhirVersion());

                // For profiles-types, we expect Kind = ComplexType | PrimitiveType
                Assert.IsNotNull(summary.GetStructureDefinitionKind());
                Assert.IsTrue(
                    summary.GetStructureDefinitionKind() == StructureDefinition.StructureDefinitionKind.ComplexType.GetLiteral()
                    ||
                    summary.GetStructureDefinitionKind() == StructureDefinition.StructureDefinitionKind.PrimitiveType.GetLiteral()
                );

                Assert.IsNotNull(summary.GetStructureDefinitionType());
                // If this is a specializing StructDef, then BaseDefinition should also be specified
                Assert.IsTrue(
                    summary.GetStructureDefinitionDerivation() != StructureDefinition.TypeDerivationRule.Specialization.GetLiteral()
                    || summary.GetStructureDefinitionBaseDefinition() != null);


                // [WMR 20180725] Also harvest root element definition text
                var rootDefinition = summary.GetStructureDefinitionRootDefinition();
                Assert.IsNotNull(rootDefinition);
            }
        }

        [TestMethod]
        public void TestProfilesResourcesXml()
        {
            const string path = @"TestData\profiles-resources.xml";

            var summaries = ArtifactSummaryGenerator.Default.Generate(path);
            Assert.IsNotNull(summaries);
            Assert.AreNotEqual(0, summaries.Count);
            for (int i = 0; i < summaries.Count; i++)
            {
                var summary = summaries[i];
                Assert.IsFalse(summary.IsFaulted);

                // Common properties
                Assert.AreEqual(path, summary.Origin);

                var fi = new FileInfo(path);
                Assert.AreEqual(fi.Length, summary.FileSize);
                Assert.AreEqual(fi.LastWriteTimeUtc, summary.LastModified);

                if (StringComparer.Ordinal.Equals(ResourceType.StructureDefinition.GetLiteral(), summary.ResourceTypeName))
                {
                    Assert.IsTrue(summary.ResourceType == ResourceType.StructureDefinition);

                    // Conformance resource properties
                    Assert.IsNotNull(summary.GetConformanceCanonicalUrl());
                    Assert.IsTrue(summary.GetConformanceCanonicalUrl().ToString().StartsWith("http://hl7.org/fhir/StructureDefinition/"));
                    var name = summary.GetConformanceName();
                    Assert.IsNotNull(name);
                    Assert.IsNotNull(summary.GetPublicationStatus());

                    // [WMR 20181213] R4 NEW - Also harvest core extensions:
                    var standardsStatus = summary.GetStructureDefinitionStandardsStatus();
                    Assert.IsNotNull(standardsStatus);
                    var isNormative = standardsStatus == "normative";
                    var normativeVersion = summary.GetStructureDefinitionNormativeVersion();
                    if (isNormative)
                    {
                        Assert.IsNotNull(normativeVersion);
                    }
                    var expectedStatus = isNormative ? PublicationStatus.Active : PublicationStatus.Draft;
                    Assert.AreEqual(expectedStatus.GetLiteral(), summary.GetPublicationStatus());

                    //Debug.WriteLine($"{summary.ResourceType} | {summary.Canonical()} | {summary.Name()}");

                    // StructureDefinition properties
                    Assert.IsNotNull(summary.GetStructureDefinitionFhirVersion());
                    Assert.AreEqual(ApiFhirVersion, summary.GetStructureDefinitionFhirVersion());

                    // For profiles-resources, we expect Kind = Resource | Logical
                    var kind = summary.GetStructureDefinitionKind();
                    Assert.IsNotNull(kind);
                    Assert.IsTrue(
                        kind == StructureDefinition.StructureDefinitionKind.Resource.GetLiteral()
                        ||
                        // e.g. for MetadataResource
                        kind == StructureDefinition.StructureDefinitionKind.Logical.GetLiteral()
                    );

                    Assert.IsNotNull(summary.GetStructureDefinitionType());

                    // If this is a specializing StructDef, then BaseDefinition should also be specified
                    var derivation = summary.GetStructureDefinitionDerivation();
                    if (derivation != null)
                    {
                        // Base definition should always be specified, except for root types such as Resource
                        Assert.IsNotNull(summary.GetStructureDefinitionBaseDefinition());
                    }

                    // [WMR 20171219] Core extensions
                    if (kind == StructureDefinition.StructureDefinitionKind.Resource.GetLiteral())
                    {
                        Assert.IsNotNull(summary.GetStructureDefinitionMaturityLevel());
                        Assert.IsNotNull(summary.GetStructureDefinitionWorkingGroup());
                    }

                    // [WMR 20180725] Also harvest root element definition text
                    var rootDefinition = summary.GetStructureDefinitionRootDefinition();
                    Assert.IsNotNull(rootDefinition);
                }

            }
        }

        ArtifactSummary assertSummary(string path, params ArtifactSummaryHarvester[] harvesters)
        {
            var summaries = ArtifactSummaryGenerator.Default.Generate(path, harvesters);
            Assert.IsNotNull(summaries);
            Assert.AreEqual(1, summaries.Count);
            var summary = summaries[0];
            Assert.IsFalse(summary.IsFaulted);
            Assert.AreEqual(path, summary.Origin);

            var fi = new FileInfo(path);
            Assert.AreEqual(fi.Length, summary.FileSize);
            Assert.AreEqual(fi.LastWriteTimeUtc, summary.LastModified);

            return summary;
        }

        [TestMethod]
        public void TestZipSummary()
        {
            var source = ZipSource.CreateValidationSource();
            var summaries = source.ListSummaries().ToList();
            Assert.IsNotNull(summaries);
            // [WMR 20181213] R4 NEW
            Assert.AreEqual(5105, summaries.Count); // STU3: 7941
            Assert.AreEqual(924, summaries.OfResourceType(ResourceType.StructureDefinition).Count()); // STU3: 581
            Assert.IsTrue(!summaries.Errors().Any());
        }

        [TestMethod]
        public void TestLoadResourceFromZipSource()
        {
            // ZipSource extracts core ZIP archive to (temp) folder, then delegates to DirectorySource
            // i.e. artifact summaries are harvested from files on disk

            var source = ZipSource.CreateValidationSource();
            var summaries = source.ListSummaries();
            var patientUrl = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient).Value;
            var patientSummary = summaries.FindConformanceResources(patientUrl).FirstOrDefault();
            Assert.IsNotNull(patientSummary);
            Assert.AreEqual(ResourceType.StructureDefinition, patientSummary.ResourceType);
            Assert.AreEqual(patientUrl, patientSummary.GetConformanceCanonicalUrl());

            Assert.IsNotNull(patientSummary.Origin);
            var patientStructure = source.LoadBySummary<StructureDefinition>(patientSummary);
            Assert.IsNotNull(patientStructure);
        }

        [TestMethod]
        public void TestLoadResourceFromZipStream()
        {
            // Harvest summaries and load artifact straight from core ZIP archive

            // Use XmlNavigatorStream to navigate resources stored inside a zip file
            // ZipDeflateStream does not support seeking (forward-only stream)
            // Therefore this only works for the XmlNavigatorStream, as the ctor does NOT (need to) call Reset()
            // JsonNavigatorStream cannot support zip streams; ctor needs to call Reset after scanning resourceType

            ArtifactSummary corePatientSummary;
            var corePatientUrl = ModelInfo.CanonicalUriForFhirCoreType(FHIRAllTypes.Patient).Value;
            string zipEntryName = "profiles-resources.xml";

            // Generate summaries from core ZIP resource definitions (extract in memory)
            using (var archive = ZipFile.Open(ZipSource.SpecificationZipFileName, ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name == zipEntryName);
                Assert.IsNotNull(entry);

                using (var entryStream = entry.Open())
                using (var navStream = new XmlNavigatorStream(entryStream))
                {
                    var summaries = ArtifactSummaryGenerator.Default.Generate(navStream);
                    Assert.IsNotNull(summaries);
                    corePatientSummary = summaries.FindConformanceResources(corePatientUrl).FirstOrDefault();
                }

            }

            Assert.IsNotNull(corePatientSummary);
            Assert.AreEqual(ResourceType.StructureDefinition, corePatientSummary.ResourceType);
            Assert.AreEqual(corePatientUrl, corePatientSummary.GetConformanceCanonicalUrl());

            // Load core Patient resource from ZIP (extract in memory)
            using (var archive = ZipFile.Open(ZipSource.SpecificationZipFileName, ZipArchiveMode.Read))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name == zipEntryName);
                using (var entryStream = entry.Open())
                using (var navStream = new XmlNavigatorStream(entryStream))
                {
                    var nav = navStream.Current;
                    if (nav != null)
                    {
                        // Parse target resource from navigator
                        var parser = new BaseFhirParser();
                        var corePatient = parser.Parse<StructureDefinition>(nav);
                        Assert.IsNotNull(corePatient);
                        Assert.AreEqual(corePatientUrl, corePatient.Url);
                    }
                }
            }

        }

    }
}
