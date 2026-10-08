const fs = require('fs');
const filePath = 'D:\\Project\\vue-demo\\apps\\web-antd\\src\\views\\orchestration\\shared\\record-form-fields.vue';
let content = fs.readFileSync(filePath, 'utf8');

// 1. Update imports
const oldImports = `import {
  Button,
  DatePicker,
  Input,
  InputNumber,
  Switch,
  Table,
} from 'ant-design-vue';`;

const newImports = `import {
  Button,
  Checkbox,
  DatePicker,
  Input,
  InputNumber,
  Radio,
  Rate,
  Select,
  Switch,
  Table,
} from 'ant-design-vue';`;

content = content.replace(oldImports, newImports);

// 2. Update props
const oldProps = `const props = withDefaults(
  defineProps<{
    fields: FormFieldDef[];
    mode: 'create' | 'detail' | 'update';
    modelValue: Record<string, unknown>;
    designer?: boolean;
    selectedField?: string;
  }>(),
  {
    designer: false,
    selectedField: '',
  },
);`;

const newProps = `const props = withDefaults(
  defineProps<{
    fields: FormFieldDef[];
    mode: 'create' | 'detail' | 'update';
    modelValue: Record<string, unknown>;
    designer?: boolean;
    selectedField?: string;
    fieldPermissions?: Record<string, string>;
  }>(),
  {
    designer: false,
    selectedField: '',
    fieldPermissions: () => ({}),
  },
);`;

content = content.replace(oldProps, newProps);

// 3. Update isReadonly & isVisible & add dependency logic
const oldHelpers = `function isReadonly(field: FormFieldDef) {
  if (props.mode === 'detail') return true;
  if (props.mode === 'create') return !!field.readonlyOnCreate;
  return !!field.readonlyOnUpdate;
}

function isVisible(field: FormFieldDef) {
  if (props.mode === 'create') return field.visibleOnCreate !== false;
  if (props.mode === 'update') return field.visibleOnUpdate !== false;
  return field.visibleOnDetail !== false;
}`;

const newHelpers = `function evaluateDependency(dep: any): boolean {
  const val = props.modelValue[dep.sourceField];
  const op = (dep.op || 'eq').toLowerCase();
  const target = dep.value;
  if (op === 'isempty') return val === undefined || val === null || val === '';
  if (op === 'isnotempty') return val !== undefined && val !== null && val !== '';
  if (op === 'eq') return String(val ?? '') === String(target ?? '');
  if (op === 'ne') return String(val ?? '') !== String(target ?? '');
  if (op === 'contains') return String(val ?? '').includes(String(target ?? ''));
  return false;
}

function isDependencyHidden(field: FormFieldDef): boolean {
  if (!field.dependencies || field.dependencies.length === 0) return false;
  for (const dep of field.dependencies) {
    const matched = evaluateDependency(dep);
    if (matched && dep.action === 'hide') return true;
    if (!matched && dep.action === 'show') return true;
  }
  return false;
}

function isDependencyReadonly(field: FormFieldDef): boolean {
  if (!field.dependencies || field.dependencies.length === 0) return false;
  for (const dep of field.dependencies) {
    const matched = evaluateDependency(dep);
    if (matched && (dep.action === 'disable' || dep.action === 'readonly')) return true;
  }
  return false;
}

function isDependencyRequired(field: FormFieldDef): boolean {
  if (!field.dependencies || field.dependencies.length === 0) return false;
  for (const dep of field.dependencies) {
    const matched = evaluateDependency(dep);
    if (matched && dep.action === 'require') return true;
  }
  return false;
}

function isReadonly(field: FormFieldDef) {
  const perm = props.fieldPermissions?.[field.field];
  if (perm === 'read') return true;
  if (perm === 'write' || perm === 'required') return false;
  if (isDependencyReadonly(field)) return true;
  if (props.mode === 'detail') return true;
  if (props.mode === 'create') return !!field.readonlyOnCreate;
  return !!field.readonlyOnUpdate;
}

function isVisible(field: FormFieldDef) {
  const perm = props.fieldPermissions?.[field.field];
  if (perm === 'hide') return false;
  if (isDependencyHidden(field)) return false;
  if (props.mode === 'create') return field.visibleOnCreate !== false;
  if (props.mode === 'update') return field.visibleOnUpdate !== false;
  return field.visibleOnDetail !== false;
}

function isFieldMandatory(field: FormFieldDef) {
  const perm = props.fieldPermissions?.[field.field];
  if (perm === 'required') return true;
  if (isDependencyRequired(field)) return true;
  return isFieldRequired(field, props.mode);
}`;

content = content.replace(oldHelpers, newHelpers);

// 4. Update required indicator in template
content = content.replace(
  `isFieldRequired(field, mode)`,
  `isFieldMandatory(field)`
);

// 5. Add new control templates before default Input
const oldDefaultInput = `      <Input
        v-else
        :disabled="isReadonly(field) || designer"
        :placeholder="field.placeholder"
        :value="String(modelValue[field.field] ?? '')"
        @update:value="(v) => patch(field.field, v)"
      />`;

const newDefaultInput = `      <Select
        v-else-if="field.control === 'userPicker'"
        :disabled="isReadonly(field) || designer"
        :placeholder="field.placeholder || '选择审批人员'"
        :value="modelValue[field.field] as any"
        :options="[
          { label: '管理员 (admin)', value: 'admin' },
          { label: '张三 (zhangsan)', value: 'zhangsan' },
          { label: '李四 (lisi)', value: 'lisi' },
          { label: '王五 (wangwu)', value: 'wangwu' }
        ]"
        show-search
        class="w-full"
        @update:value="(v) => patch(field.field, v)"
      />
      <Select
        v-else-if="field.control === 'deptPicker'"
        :disabled="isReadonly(field) || designer"
        :placeholder="field.placeholder || '选择部门'"
        :value="modelValue[field.field] as any"
        :options="[
          { label: '总经办', value: 'gm' },
          { label: '研发部', value: 'rd' },
          { label: '财务部', value: 'finance' },
          { label: '人事行政部', value: 'hr' }
        ]"
        class="w-full"
        @update:value="(v) => patch(field.field, v)"
      />
      <Select
        v-else-if="field.control === 'select'"
        :disabled="isReadonly(field) || designer"
        :placeholder="field.placeholder || '请选择'"
        :value="modelValue[field.field] as any"
        :options="field.controlProps?.options || [
          { label: '选项 1', value: 'opt1' },
          { label: '选项 2', value: 'opt2' }
        ]"
        class="w-full"
        @update:value="(v) => patch(field.field, v)"
      />
      <Radio.Group
        v-else-if="field.control === 'radio'"
        :disabled="isReadonly(field) || designer"
        :value="modelValue[field.field] as any"
        :options="field.controlProps?.options || [
          { label: '是 / 选项 A', value: 'A' },
          { label: '否 / 选项 B', value: 'B' }
        ]"
        @update:value="(v) => patch(field.field, (v as any)?.target ? (v as any).target.value : v)"
      />
      <Checkbox.Group
        v-else-if="field.control === 'checkbox'"
        :disabled="isReadonly(field) || designer"
        :value="(modelValue[field.field] as any) || []"
        :options="field.controlProps?.options || [
          { label: '选项 A', value: 'A' },
          { label: '选项 B', value: 'B' }
        ]"
        @update:value="(v) => patch(field.field, v)"
      />
      <Rate
        v-else-if="field.control === 'rate'"
        :disabled="isReadonly(field) || designer"
        :value="Number(modelValue[field.field] || 0)"
        @update:value="(v) => patch(field.field, v)"
      />
      <div v-else-if="field.control === 'upload'" class="flex items-center gap-2">
        <Input
          :disabled="isReadonly(field) || designer"
          :placeholder="field.placeholder || '附件名称或链接'"
          :value="String(modelValue[field.field] ?? '')"
          class="flex-1"
          @update:value="(v) => patch(field.field, v)"
        />
        <Button size="small" :disabled="isReadonly(field) || designer">上传</Button>
      </div>
      <Input
        v-else
        :disabled="isReadonly(field) || designer"
        :placeholder="field.placeholder"
        :value="String(modelValue[field.field] ?? '')"
        @update:value="(v) => patch(field.field, v)"
      />`;

content = content.replace(oldDefaultInput, newDefaultInput);

fs.writeFileSync(filePath, content, 'utf8');
console.log('Successfully patched record-form-fields.vue');
