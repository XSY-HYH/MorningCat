import { Button } from '@heroui/button';
import { Card, CardBody } from '@heroui/card';
import { Chip } from '@heroui/chip';
import { Divider } from '@heroui/divider';
import { Input, Textarea } from '@heroui/input';
import { Listbox, ListboxItem } from '@heroui/listbox';
import { Modal, ModalContent, ModalHeader, ModalBody, ModalFooter } from '@heroui/modal';
import { ScrollShadow } from '@heroui/scroll-shadow';
import { Spinner } from '@heroui/spinner';
import { Tab, Tabs } from '@heroui/tabs';
import { Table, TableHeader, TableColumn, TableBody, TableRow, TableCell } from '@heroui/table';
import { useLocalStorage } from '@uidotdev/usehooks';
import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';

import key from '@/const/key';

interface DatabaseEntry {
  key: string;
  id: string;
  pluginClassName: string;
  databasePath: string;
  databaseType: string;
  fileSize: number;
  tables: string[];
}

interface ColumnInfo {
  name: string;
  type: string;
  notNull: boolean;
  isPrimaryKey: boolean;
  defaultValue: string;
}

interface DatabaseDetail {
  key: string;
  id: string;
  pluginClassName: string;
  databasePath: string;
  databaseType: string;
  fileSize: number;
  tables: string[];
  tableColumns: Record<string, ColumnInfo[]>;
}

export default function DatabasePage () {
  const [databases, setDatabases] = useState<DatabaseEntry[]>([]);
  const [selectedDb, setSelectedDb] = useState<string | null>(null);
  const [detail, setDetail] = useState<DatabaseDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [selectedTable, setSelectedTable] = useState<string | null>(null);
  const [tableData, setTableData] = useState<Record<string, unknown>[]>([]);
  const [tableLoading, setTableLoading] = useState(false);
  const [sqlModalOpen, setSqlModalOpen] = useState(false);
  const [sqlInput, setSqlInput] = useState('');
  const [sqlResult, setSqlResult] = useState<Record<string, unknown>[] | null>(null);
  const [sqlAffected, setSqlAffected] = useState<number | null>(null);
  const [sqlRunning, setSqlRunning] = useState(false);

  useEffect(() => {
    loadDatabases();
  }, []);

  useEffect(() => {
    if (selectedDb) {
      loadDetail(selectedDb);
    } else {
      setDetail(null);
      setSelectedTable(null);
      setTableData([]);
    }
  }, [selectedDb]);

  const loadDatabases = async () => {
    try {
      const response = await fetch('/api/database/list');
      const result = await response.json();
      if (result.code === 0) {
        setDatabases(result.data || []);
      }
    } catch {
      toast.error('加载数据库列表失败');
    } finally {
      setLoading(false);
    }
  };

  const loadDetail = async (dbKey: string) => {
    setDetailLoading(true);
    try {
      const response = await fetch(`/api/database/detail?key=${encodeURIComponent(dbKey)}`);
      const result = await response.json();
      if (result.code === 0) {
        setDetail(result.data);
        if (result.data?.tables?.length > 0) {
          setSelectedTable(result.data.tables[0]);
        } else {
          setSelectedTable(null);
        }
      }
    } catch {
      toast.error('加载数据库详情失败');
    } finally {
      setDetailLoading(false);
    }
  };

  useEffect(() => {
    if (selectedDb && selectedTable) {
      loadTableData(selectedDb, selectedTable);
    } else {
      setTableData([]);
    }
  }, [selectedTable]);

  const loadTableData = async (dbKey: string, tableName: string) => {
    setTableLoading(true);
    try {
      const token = localStorage.getItem(key.token);
      const response = await fetch('/api/database/query', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${JSON.parse(token)}` } : {}),
        },
        body: JSON.stringify({ key: dbKey, sql: `SELECT * FROM "${tableName}" LIMIT 100` }),
      });
      const result = await response.json();
      if (result.code === 0) {
        setTableData(result.data || []);
      } else {
        toast.error(result.message || '查询失败');
        setTableData([]);
      }
    } catch {
      toast.error('查询表数据失败');
      setTableData([]);
    } finally {
      setTableLoading(false);
    }
  };

  const handleExecuteSql = async () => {
    if (!selectedDb || !sqlInput.trim()) return;
    setSqlRunning(true);
    setSqlResult(null);
    setSqlAffected(null);
    try {
      const token = localStorage.getItem(key.token);
      const trimmedSql = sqlInput.trim().toUpperCase();
      const isQuery = trimmedSql.startsWith('SELECT') || trimmedSql.startsWith('PRAGMA');

      const response = await fetch(isQuery ? '/api/database/query' : '/api/database/execute', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${JSON.parse(token)}` } : {}),
        },
        body: JSON.stringify({ key: selectedDb, sql: sqlInput.trim() }),
      });
      const result = await response.json();
      if (result.code === 0) {
        if (isQuery) {
          setSqlResult(result.data || []);
          toast.success(`查询返回 ${(result.data || []).length} 行`);
        } else {
          setSqlAffected(result.data?.affected ?? 0);
          toast.success(`影响 ${result.data?.affected ?? 0} 行`);
          if (detail) {
            loadDetail(selectedDb);
          }
        }
      } else {
        toast.error(result.message || '执行失败');
      }
    } catch {
      toast.error('执行SQL失败');
    } finally {
      setSqlRunning(false);
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const [backgroundImage] = useLocalStorage<string>(key.backgroundImage, '');
  const hasBackground = !!backgroundImage;

  if (loading) {
    return (
      <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative flex items-center justify-center min-h-[400px]'>
        <Spinner size='lg' />
      </section>
    );
  }

  return (
    <section className='w-full max-w-[1200px] mx-auto py-4 md:py-8 px-2 md:px-6 relative'>
      <title>数据库管理 - MorningCat WebUI</title>

      <div className='flex flex-col md:flex-row gap-4'>
        <div className='w-full md:w-64 shrink-0'>
          <Card className={`backdrop-blur-sm border shadow-sm rounded-2xl ${hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'}`}>
            <CardBody className='p-2'>
              <h3 className='text-sm font-semibold px-2 py-1 text-default-500'>数据库列表</h3>
              {databases.length > 0 ? (
                <ScrollShadow className='h-[500px]'>
                  <Listbox
                    aria-label='数据库列表'
                    selectionMode='single'
                    selectedKeys={selectedDb ? new Set([selectedDb]) : new Set()}
                    onSelectionChange={(keys) => {
                      const selected = Array.from(keys)[0] as string;
                      setSelectedDb(selected);
                    }}
                  >
                    {databases.map((db) => (
                      <ListboxItem key={db.key} textValue={db.key}>
                        <div className='flex items-center gap-2'>
                          <span className='truncate text-sm'>{db.pluginClassName || db.id}</span>
                          <Chip size='sm' variant='flat' color='primary'>{db.databaseType}</Chip>
                        </div>
                      </ListboxItem>
                    ))}
                  </Listbox>
                </ScrollShadow>
              ) : (
                <div className='p-4 text-center text-default-400 text-sm'>暂无数据库</div>
              )}
            </CardBody>
          </Card>
        </div>

        <div className='flex-1 flex flex-col gap-4'>
          {detailLoading ? (
            <div className='flex items-center justify-center h-[400px]'>
              <Spinner size='lg' />
            </div>
          ) : detail ? (
            <>
              <Card className={`backdrop-blur-sm border shadow-sm rounded-2xl ${hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'}`}>
                <CardBody className='p-4'>
                  <div className='flex items-center justify-between mb-3'>
                    <div>
                      <h3 className='text-lg font-semibold'>{detail.pluginClassName || detail.id}</h3>
                      <div className='flex items-center gap-2 mt-1'>
                        <Chip size='sm' variant='flat' color='primary'>{detail.databaseType}</Chip>
                        <span className='text-xs text-default-400'>{formatFileSize(detail.fileSize)}</span>
                        <span className='text-xs text-default-400'>{detail.tables.length} 个表</span>
                      </div>
                    </div>
                    <Button color='primary' size='sm' onPress={() => { setSqlInput(''); setSqlResult(null); setSqlAffected(null); setSqlModalOpen(true); }}>
                      执行SQL
                    </Button>
                  </div>
                  <Divider className='mb-3' />
                  <div className='text-xs text-default-400 mb-1'>路径: {detail.databasePath}</div>
                </CardBody>
              </Card>

              <Card className={`backdrop-blur-sm border shadow-sm rounded-2xl ${hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'}`}>
                <CardBody className='p-4'>
                  <Tabs
                    selectedKey={selectedTable || ''}
                    onSelectionChange={(key) => setSelectedTable(key as string)}
                    variant='underlined'
                  >
                    {detail.tables.map((table) => (
                      <Tab key={table} title={table} />
                    ))}
                  </Tabs>

                  {selectedTable && detail.tableColumns[selectedTable] && (
                    <div className='mt-3'>
                      <h4 className='text-sm font-medium text-default-500 mb-2'>列信息</h4>
                      <div className='flex flex-wrap gap-2 mb-3'>
                        {detail.tableColumns[selectedTable].map((col) => (
                          <Chip
                            key={col.name}
                            size='sm'
                            variant='flat'
                            color={col.isPrimaryKey ? 'warning' : 'default'}
                          >
                            {col.name} <span className='text-default-400'>({col.type})</span>
                            {col.notNull && <span className='text-danger-500'>*</span>}
                          </Chip>
                        ))}
                      </div>
                      <Divider className='mb-3' />
                      <h4 className='text-sm font-medium text-default-500 mb-2'>数据 (最多100行)</h4>
                      {tableLoading ? (
                        <div className='flex items-center justify-center h-[100px]'>
                          <Spinner />
                        </div>
                      ) : tableData.length > 0 ? (
                        <ScrollShadow className='max-h-[400px]'>
                          <Table aria-label='表数据' isCompact removeWrapper>
                            <TableHeader>
                              {Object.keys(tableData[0]).map((col) => (
                                <TableColumn key={col}>{col}</TableColumn>
                              ))}
                            </TableHeader>
                            <TableBody>
                              {tableData.map((row, idx) => (
                                <TableRow key={idx}>
                                  {Object.keys(tableData[0]).map((col) => (
                                    <TableCell key={col}>
                                      <span className='text-xs'>{row[col] === null ? <span className='text-default-300'>NULL</span> : String(row[col])}</span>
                                    </TableCell>
                                  ))}
                                </TableRow>
                              ))}
                            </TableBody>
                          </Table>
                        </ScrollShadow>
                      ) : (
                        <div className='text-center text-default-400 text-sm py-4'>空表</div>
                      )}
                    </div>
                  )}
                </CardBody>
              </Card>
            </>
          ) : (
            <Card className={`backdrop-blur-sm border shadow-sm rounded-2xl ${hasBackground ? 'bg-white/20 dark:bg-black/10 border-white/40 dark:border-white/10' : 'bg-white/60 dark:bg-black/40 border-white/40 dark:border-white/10'}`}>
              <CardBody className='flex items-center justify-center h-[300px]'>
                <span className='text-default-400'>选择左侧数据库查看详情</span>
              </CardBody>
            </Card>
          )}
        </div>
      </div>

      <Modal isOpen={sqlModalOpen} onClose={() => setSqlModalOpen(false)} size='3xl'>
        <ModalContent>
          <ModalHeader>执行SQL - {detail?.pluginClassName || detail?.id}</ModalHeader>
          <ModalBody>
            <Textarea
              value={sqlInput}
              onValueChange={setSqlInput}
              placeholder='输入SQL语句...'
              minRows={4}
              maxRows={8}
              className='font-mono'
            />
            {sqlResult !== null && sqlResult.length > 0 && (
              <ScrollShadow className='max-h-[300px]'>
                <Table aria-label='查询结果' isCompact removeWrapper>
                  <TableHeader>
                    {Object.keys(sqlResult[0]).map((col) => (
                      <TableColumn key={col}>{col}</TableColumn>
                    ))}
                  </TableHeader>
                  <TableBody>
                    {sqlResult.map((row, idx) => (
                      <TableRow key={idx}>
                        {Object.keys(sqlResult[0]).map((col) => (
                          <TableCell key={col}>
                            <span className='text-xs'>{row[col] === null ? 'NULL' : String(row[col])}</span>
                          </TableCell>
                        ))}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </ScrollShadow>
            )}
            {sqlAffected !== null && (
              <div className='text-sm text-default-500'>影响 {sqlAffected} 行</div>
            )}
          </ModalBody>
          <ModalFooter>
            <Button variant='light' onPress={() => setSqlModalOpen(false)}>关闭</Button>
            <Button color='primary' onPress={handleExecuteSql} isLoading={sqlRunning}>执行</Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </section>
  );
}
