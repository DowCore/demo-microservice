const fs = require('fs');
const filePath = 'D:\\Project\\vue-demo\\apps\\web-antd\\src\\views\\orchestration\\shared\\form-schema.ts';
let content = fs.readFileSync(filePath, 'utf8');

// Update CONTROLS
const targetControls = `const CONTROLS = new Set([
  'date',
  'datetime',
  'input',
  'list',
  'number',
  'switch',
  'table',
  'textarea',
  'tree',
]);`;

const replacementControls = `const CONTROLS = new Set([
  'date',
  'datetime',
  'input',
  'list',
  'number',
  'switch',
  'table',
  'textarea',
  'tree',
  'radio',
  'checkbox',
  'select',
  'userPicker',
  'deptPicker',
  'upload',
  'rate',
]);`;

content = content.replace(targetControls, replacementControls);

// Update sampleCell
const targetSample = `function sampleCell(field: FormFieldDef) {
  if (field.control === 'number') return 1;
  if (field.control === 'switch') return true;
  return \`示例\${field.title || field.field}\`;
}`;

const replacementSample = `function sampleCell(field: FormFieldDef) {
  if (field.control === 'number') return 1;
  if (field.control === 'switch') return true;
  if (field.control === 'radio' || field.control === 'select') return '选项1';
  if (field.control === 'checkbox') return ['选项1'];
  if (field.control === 'userPicker') return 'admin';
  if (field.control === 'deptPicker') return '总经办';
  if (field.control === 'upload') return 'attachment.pdf';
  if (field.control === 'rate') return 5;
  return \`示例\${field.title || field.field}\`;
}`;

content = content.replace(targetSample, replacementSample);

// Update normalizeField
const targetNorm = `    treeChildrenField:
      control === 'tree'
        ? String(raw.treeChildrenField || 'children')
        : undefined,
  };`;

const replacementNorm = `    treeChildrenField:
      control === 'tree'
        ? String(raw.treeChildrenField || 'children')
        : undefined,
    dependencies: Array.isArray(raw.dependencies) ? raw.dependencies : undefined,
    controlProps: raw.controlProps && typeof raw.controlProps === 'object' ? { ...raw.controlProps } : undefined,
  };`;

content = content.replace(targetNorm, replacementNorm);

fs.writeFileSync(filePath, content, 'utf8');
console.log('Successfully patched form-schema.ts');
