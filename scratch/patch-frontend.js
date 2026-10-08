const fs = require('fs');
const path = require('path');

const filePath = 'D:\\Project\\vue-demo\\apps\\web-antd\\src\\api\\saas\\orchestration.ts';
let content = fs.readFileSync(filePath, 'utf8');

// 1. Add FormFieldDependencyDef and update FormFieldDef
if (!content.includes('export interface FormFieldDependencyDef')) {
  const target = 'export interface FormFieldDef {';
  const replacement = `export interface FormFieldDependencyDef {
  sourceField: string;
  op?: string;
  value?: string;
  action?: 'show' | 'hide' | 'enable' | 'disable' | 'require' | 'optional' | 'setValue';
  setValue?: string;
}

export interface FormFieldDef {`;
  content = content.replace(target, replacement);

  // Add dependencies & controlProps to FormFieldDef
  const fieldEndTarget = 'treeChildrenField?: string;\n}';
  const fieldEndReplacement = `treeChildrenField?: string;
  dependencies?: FormFieldDependencyDef[];
  controlProps?: Record<string, any>;
}`;
  content = content.replace(fieldEndTarget, fieldEndReplacement);

  // Add dependencies & controlProps to FormWidgetDef
  const widgetEndTarget = 'requiredOnUpdate?: boolean;\n}';
  const widgetEndReplacement = `requiredOnUpdate?: boolean;
  dependencies?: FormFieldDependencyDef[];
  controlProps?: Record<string, any>;
}`;
  content = content.replace(widgetEndTarget, widgetEndReplacement);
}

// 2. Update WorkflowTask
if (!content.includes('fieldPermissions?: Record<string, string>;')) {
  const taskTarget = '  opinion?: string;\n}';
  const taskReplacement = `  opinion?: string;
  fieldPermissions?: Record<string, string>;
}`;
  content = content.replace(taskTarget, taskReplacement);
}

// 3. Update WorkflowInstance
if (!content.includes('processSnapshotJson?: string;')) {
  const instanceTarget = '  recordJson: string;\n  tasks: WorkflowTask[];';
  const instanceReplacement = `  recordJson: string;
  processSnapshotJson?: string;
  historyJson?: string;
  tasks: WorkflowTask[];`;
  content = content.replace(instanceTarget, instanceReplacement);
}

// 4. Update completeWorkflowTaskApi
const completeTarget = `export function completeWorkflowTaskApi(
  taskId: string,
  data: { pass: boolean; opinion?: string },
)`;
const completeReplacement = `export function completeWorkflowTaskApi(
  taskId: string,
  data: {
    pass?: boolean;
    opinion?: string;
    action?: 'approve' | 'reject_to_prev' | 'reject_to_starter' | 'reject_terminate' | 'transfer';
    transferUserName?: string;
    targetNodeId?: string;
    recordPatch?: Record<string, any>;
  },
)`;

if (content.includes(completeTarget)) {
  content = content.replace(completeTarget, completeReplacement);
}

fs.writeFileSync(filePath, content, 'utf8');
console.log('Successfully patched orchestration.ts');
