import DynamicConfigPage from './dynamic_config';

export default DynamicConfigPage;

export interface ConfigPageProps {
  children?: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
}
