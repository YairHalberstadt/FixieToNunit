using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Formatting;

namespace FixieToNunitConverter
{
    class Program
    {
        static void Main(string[] args)
		{
			var solutionPath = args[0];

			var task = RunOnSolution(solutionPath);
			task.Wait();
		}

		public static async Task RunOnSolution(string solutionPath)
		{
			using (var workspace = MSBuildWorkspace.Create())
			{
				workspace.LoadMetadataForReferencedProjects = true;
				var solution = await workspace.OpenSolutionAsync(solutionPath);
				var testProjects = solution.Projects.Where(x => x.Name.Split('.').Any(y => y == "Tests"));
				foreach (var project in testProjects)
					await FixProject(project);
				solution = await workspace.OpenSolutionAsync(solutionPath);
				testProjects = solution.Projects.Where(x => x.Name.Split('.').Any(y => y == "Tests"));
				foreach (var project in testProjects)
					await FormatProject(project, workspace);
			}
		}

		public static async Task FormatProject(Project project, Workspace workspace)
		{
			var compilation = await project.GetCompilationAsync();
			foreach (var syntaxTree in compilation.SyntaxTrees)
			{
				var compilationUnitSyntax = syntaxTree.GetCompilationUnitRoot();
				if (compilationUnitSyntax.Usings.Any(x => ((dynamic) x).Name.ToString().Contains("NUnit.Framework")))
				{
					var newSyntaxTree = Formatter.Format(compilationUnitSyntax, workspace, workspace.Options);
					File.WriteAllText(syntaxTree.FilePath, newSyntaxTree.GetText().ToString());
				}
					

			}
		}

		public static async Task RunOnProject(string projectPath)
		{
			try
			{
				using (var workspace = MSBuildWorkspace.Create())
				{
					workspace.LoadMetadataForReferencedProjects = true;
					var project = await workspace.OpenProjectAsync(projectPath);
					ImmutableList<WorkspaceDiagnostic> diagnostics = workspace.Diagnostics;
					foreach (var diagnostic in diagnostics)
					{
						Console.WriteLine(diagnostic.Message);
					}

					await FixProject(project);

					project = await workspace.OpenProjectAsync(projectPath);
					diagnostics = workspace.Diagnostics;
					foreach (var diagnostic in diagnostics)
					{
						Console.WriteLine(diagnostic.Message);
					}

					await FormatProject(project, workspace);
				}

			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		public static async Task FixProject(Project project)
		{
			var compilation = await project.GetCompilationAsync();

			var classVisitor = new ClassReWriter();

			foreach (var syntaxTree in compilation.SyntaxTrees)
			{
				var newSyntaxTree = classVisitor.Visit(syntaxTree.GetRoot());
				if (syntaxTree.GetRoot() != newSyntaxTree)
				{
					var compilationUnitSyntax = (CompilationUnitSyntax)(newSyntaxTree);
					var withUsings = compilationUnitSyntax.Usings.Any(x => ((dynamic)x).Name.ToString().Contains("NUnit.Framework")) 
						? compilationUnitSyntax 
						: compilationUnitSyntax.AddUsings(SyntaxFactory.UsingDirective(
						SyntaxFactory.QualifiedName(SyntaxFactory.IdentifierName("NUnit"),
							SyntaxFactory.IdentifierName("Framework")).WithLeadingTrivia(
							SyntaxFactory.Space)));
					File.WriteAllText(syntaxTree.FilePath, withUsings.GetText().ToString());
				}
			}
		}
    }

	class ClassReWriter : CSharpSyntaxRewriter
	{
		public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			node = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);

			if (!node.Identifier.ValueText.EndsWith("Tests"))
				return node;


			var nodeWithAttributes = node.AttributeLists.SelectMany(x => x.Attributes).Any()? node : node.AddAttributeLists(new AttributeListSyntax[]
			{
				SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("TestFixture"))))
			});

			MethodRewriter methodRewriter = new MethodRewriter();

			var nodeWithMethodsChanged = methodRewriter.Visit(nodeWithAttributes);

			return nodeWithMethodsChanged;
		}
	}

	class MethodRewriter : CSharpSyntaxRewriter
	{
		public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

			if (!node.Modifiers.Any(x => x.ValueText == "public"))
				return node;

			if ((node.ReturnType as PredefinedTypeSyntax)?.Keyword.ValueText != "void"
				&& !(node.Modifiers.Any(x => x.ValueText == "async") && node.ReturnType.ToFullString().Contains("Task")) )
				return node;

			if (node.ParameterList.Parameters.Any())
				return node;

			if (node.AttributeLists.SelectMany(x => x.Attributes).Any())
				return node;

			return node.AddAttributeLists(new AttributeListSyntax[]
			{
				SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Test"))))
			});
		}
	}

}
