# FixieToNunit
Convert Fixie Tests To Nunit Tests

This is a little tool I wrote when my company was moving from Fixie tests to NUnit.

It uses Roslyn to find classes and test methods which match Fixie's default convention, and have no attributes, and adds NUnit test attributes to them, and `using NUnit.Framework` to the using directives.

This is built for my personal needs, but with a bit of editing I think this could be useful for other people as well.

It takes as a command line argument the path to the solution, and assumes that test projects are ones with .Tests in their name eg FixieToNunit.Tests.Unit
