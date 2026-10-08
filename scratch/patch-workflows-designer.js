const fs = require('fs');
const filePath = 'D:\\Project\\vue-demo\\apps\\web-antd\\src\\views\\orchestration\\workflows\\designer.vue';
let content = fs.readFileSync(filePath, 'utf8');

// 1. Add fields to reactive selected
const oldSelected = `  multiRatio: 100,
  conditionText: '',
  leaveCondition: emptyBlock(),
  edgeCondition: emptyBlock(),
});`;

const newSelected = `  multiRatio: 100,
  emptyFallback: 'admin',
  fieldPermissionsText: '',
  conditionText: '',
  leaveCondition: emptyBlock(),
  edgeCondition: emptyBlock(),
});`;

content = content.replace(oldSelected, newSelected);

// 2. In fillSelected, load emptyFallback and fieldPermissionsText
const oldFill = `  selected.multi = (data.multi as string) || 'single';
  selected.multiRatio = Number(data.multiRatio || 100);`;

const newFill = `  selected.multi = (data.multi as string) || 'single';
  selected.multiRatio = Number(data.multiRatio || 100);
  selected.emptyFallback = (data.emptyFallback as string) || 'admin';
  selected.fieldPermissionsText = typeof data.fieldPermissions === 'object' && data.fieldPermissions
    ? JSON.stringify(data.fieldPermissions, null, 2)
    : '';`;

content = content.replace(oldFill, newFill);

// 3. In pushSelection, pass emptyFallback and fieldPermissions
const oldPush = `      multi: selected.multi,
      multiRatio: selected.multiRatio,
      conditionText: selected.conditionText,`;

const newPush = `      multi: selected.multi,
      multiRatio: selected.multiRatio,
      emptyFallback: selected.emptyFallback || 'admin',
      fieldPermissions: (() => {
        if (!selected.fieldPermissionsText?.trim()) return {};
        try { return JSON.parse(selected.fieldPermissionsText.trim()); } catch { return {}; }
      })(),
      conditionText: selected.conditionText,`;

content = content.replace(oldPush, newPush);

// 4. In template, add emptyFallback and fieldPermissions inputs under approver
const targetFormItem = `              <Form.Item v-if="selected.multi === 'ratio'" label="通过比例">
                <Input
                  :value="String(selected.multiRatio ?? 100)"
                  suffix="%"
                  @update:value="
                    (v) => (selected.multiRatio = Number(v) || 100)
                  "
                />
              </Form.Item>`;

const replacementFormItem = `              <Form.Item v-if="selected.multi === 'ratio'" label="通过比例">
                <Input
                  :value="String(selected.multiRatio ?? 100)"
                  suffix="%"
                  @update:value="
                    (v) => (selected.multiRatio = Number(v) || 100)
                  "
                />
              </Form.Item>
              <Form.Item label="候选人为空时">
                <Select
                  v-model:value="selected.emptyFallback"
                  class="w-full"
                  :options="[
                    { label: '自动转交管理员 (admin)', value: 'admin' },
                    { label: '自动跳过该节点', value: 'skip' },
                    { label: '提示报错并阻断', value: 'error' },
                  ]"
                />
              </Form.Item>
              <Form.Item label="表单字段权限 (JSON)">
                <Input.TextArea
                  v-model:value="selected.fieldPermissionsText"
                  :rows="3"
                  placeholder='{&quot;title&quot;: &quot;read&quot;, &quot;remark&quot;: &quot;write&quot;}'
                  class="font-mono text-xs"
                />
                <p class="text-[11px] text-muted-foreground mt-0.5">
                  按字段设置 read (只读) / write (可编辑) / hide (隐藏) / required (必填)
                </p>
              </Form.Item>`;

content = content.replace(targetFormItem, replacementFormItem);

fs.writeFileSync(filePath, content, 'utf8');
console.log('Successfully patched workflows/designer.vue');
