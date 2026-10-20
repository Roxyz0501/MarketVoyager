using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var ops = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => (ushort)o.Value);
string Name(EntityHandle h) => h.Kind switch {
 HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)h).Name),
 HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)h).Name),
 HandleKind.FieldDefinition => md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)h).Name),
 HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)h).Name), _ => h.Kind.ToString() };
foreach(var th in md.TypeDefinitions) {
 var type = md.GetTypeDefinition(th); string tn = md.GetString(type.Name);
 foreach(var mh in type.GetMethods()) {
 var method = md.GetMethodDefinition(mh); var mn = md.GetString(method.Name);
 if(!args.Skip(1).Any(term => (tn+"."+mn).Contains(term, StringComparison.Ordinal))) continue;
 Console.WriteLine(tn+"."+mn+"("+string.Join(", ",method.GetParameters().Select(p=>md.GetString(md.GetParameter(p).Name)))+")");
 if(method.RelativeVirtualAddress == 0) continue;
 var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
 for(int p=0;p<bytes.Length;) {
 int offset=p; ushort code=bytes[p++]; if(code==0xfe) code=(ushort)(0xfe00|bytes[p++]);
 var op=ops[code]; int size=op.OperandType switch { OperandType.InlineNone=>0, OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget=>1, OperandType.InlineVar=>2, OperandType.InlineI8 or OperandType.InlineR=>8, OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(bytes,p), _=>4 };
 string detail="";
 if(op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok) detail=Name(MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes,p)));
 else if(size == 1) detail=bytes[p].ToString();
 else if(size == 4 && op.OperandType != OperandType.InlineString) detail=BitConverter.ToInt32(bytes,p).ToString();
 if(detail.Length>0 || op.Name.StartsWith("ldc.") || op.Name.StartsWith("ldarg.")) Console.WriteLine($"  {offset:x4} {op.Name} {detail}");
 p+=size;
 }
 }
}
