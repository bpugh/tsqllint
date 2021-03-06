using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TSQLLint.Infrastructure.Parser
{
    public class DynamicSQLParser : TSqlFragmentVisitor
    {
        private readonly Action<string, int, int> callback;
        private string executableSql = string.Empty;
        private Dictionary<string, string> VariableValues = new Dictionary<string, string>();

        private int DynamicSQLStartingLine { get; set; }
        
        private int DynamicSQLStartingColumn { get; set; }

        public DynamicSQLParser(Action<string, int, int> callback)
        {
            this.callback = callback;
        }

        public override void Visit(TSqlBatch node)
        {
            var variableVisitor = new VariableVisitor();
            node.Accept(variableVisitor);
            VariableValues = variableVisitor.VariableValues;
        }

        public override void Visit(ExecuteStatement node)
        {
            DynamicSQLStartingColumn = node.ExecuteSpecification.ExecutableEntity.StartColumn;
            DynamicSQLStartingLine = node.ExecuteSpecification.ExecutableEntity.StartLine;

            var visitor = new VariableVisitor();
            node.Accept(visitor);

            var executableStrings = node.ExecuteSpecification.ExecutableEntity as ExecutableStringList;
            if (executableStrings?.Strings == null)
            {
                return;
            }

            var counter = 0;
            foreach (var executableString in executableStrings.Strings)
            {
                counter++;
                if (executableString is StringLiteral literal)
                {
                    HandleLiteral(counter, executableStrings.Strings.Count, literal);
                }
                else if (executableString is VariableReference variableReference)
                {
                    HandleVariable(counter, executableStrings.Strings.Count, variableReference);
                }
            }
        }

        private void HandleVariable(int counter, int executableCount, VariableReference variableReference)
        {
            if (!VariableValues.ContainsKey(variableReference.Name) || !VariableValues.TryGetValue(variableReference.Name, out var value))
            {
                return;
            }

            executableSql += value;
            if (counter == executableCount)
            {
                callback(executableSql, DynamicSQLStartingLine, DynamicSQLStartingColumn);
            }
        }

        private void HandleLiteral(int counter, int executableCount, StringLiteral literal)
        {
            executableSql += literal.Value;
            if (counter == executableCount)
            {
                callback(executableSql, DynamicSQLStartingLine, DynamicSQLStartingColumn);
            }
        }
    }

    public class VariableVisitor : TSqlFragmentVisitor
    {
        public VariableVisitor()
        {
            VariableValues = new Dictionary<string, string>();
        }

        public Dictionary<string, string> VariableValues { get; }
        
        public override void Visit(SetVariableStatement node)
        {
            if (node.Expression is StringLiteral strLiteral)
            {
                VariableValues[node.Variable.Name] = strLiteral.Value;
            }
            else if (node.Expression is IntegerLiteral intLiteral)
            {
                VariableValues[node.Variable.Name] = intLiteral.Value;
            }
            else if (node.Expression is BinaryExpression binaryExpression)
            {
                HandleBinaryExpression(node.Variable.Name, binaryExpression);
            }
        }

        private void HandleBinaryExpression(string name, BinaryExpression expression)
        {
            if (expression.BinaryExpressionType != BinaryExpressionType.Add)
            {
                return;
            }
            
            if (expression.FirstExpression is StringLiteral first
                && expression.SecondExpression is StringLiteral second)
            {
                VariableValues[name] = first.Value + second.Value;
            }
        }
    }
}
