import React, { useState, ChangeEvent, useEffect } from "react";
import EcommerceCard from "@/Component/Dashboard/EcommerceDashboard/EcommerceCard";
import Effortless from "@/Component/Dashboard/ProjectDashboard/Effortless";
import ProjectCard from "@/Component/Dashboard/ProjectDashboard/ProjectCard";
import ProjectFileData from "@/Component/Dashboard/ProjectDashboard/ProjectFileData";
import {
  Card,
  CardBody,
  Col,
  Container,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownToggle,
  Form,
  Input,
  Row,
} from "reactstrap";
import { EcommerceOrderData } from "@/Data/Charts/ApexCharts/ApexChart";
import Loading from "@/app/loading";

interface ProductCategory {
  id: number;
  name: string;
  sales: string;
  count: number;
  bgClass: string;
  textClass: string;
}

const WidgetsPage: React.FC = () => {
  const [ApexCharts, setApexCharts] = useState<any>(null);

  useEffect(() => {
    const loadModules = async () => {
      const [chartModule] = await Promise.all([import("react-apexcharts")]);

      setApexCharts(() => chartModule.default || chartModule);
    };

    if (typeof window !== "undefined") {
      loadModules();
    }
  }, []);
  const productCategories: ProductCategory[] = [
    {
      id: 1,
      name: "Clothing & Accessories",
      sales: "$5,000",
      count: 5641,
      bgClass: "bg-info-300",
      textClass: "text-info-dark",
    },
    {
      id: 2,
      name: "Home & Kitchen",
      sales: "$5,000",
      count: 10000,
      bgClass: "bg-primary-300",
      textClass: "text-primary-dark",
    },
    {
      id: 3,
      name: "Electronics",
      sales: "$5,000",
      count: 6897,
      bgClass: "bg-danger-300",
      textClass: "text-danger-dark",
    },
    {
      id: 4,
      name: "Jewellery",
      sales: "$5,000",
      count: 4548,
      bgClass: "bg-warning-300",
      textClass: "text-warning-dark",
    },
  ];

  const [dropdownOpen, setDropdownOpen] = useState<boolean>(false);
  const [selectedFilter, setSelectedFilter] = useState<string>("Filter");

  const toggleDropdown = (): void => setDropdownOpen(!dropdownOpen);
  const handleFilterChange = (e: ChangeEvent<HTMLInputElement>): void => {
    setSelectedFilter(e.target.value);
  };

  return (
    <Container fluid className="mt-3">
      <Row>
        <EcommerceCard />

        <Col md="6" lg="4" xxl="3">
          <Card className="product-category-card">
            <CardBody>
              <div className="d-flex justify-content-between align-items-center">
                <Dropdown isOpen={dropdownOpen} toggle={toggleDropdown}>
                  <DropdownToggle
                    tag="a"
                    className="text-dark"
                    href="#"
                    onClick={(e) => e.preventDefault()}
                  >
                    <i className="iconoir-align-left f-s-20 f-w-600 text-dark-dark"></i>
                    <i className="ti ti-chevron-down ms-1 f-s-18 align-top"></i>
                  </DropdownToggle>
                  <DropdownMenu end>
                    <DropdownItem>Last Month</DropdownItem>
                    <DropdownItem>Last Week</DropdownItem>
                    <DropdownItem>Last Year</DropdownItem>
                  </DropdownMenu>
                </Dropdown>

                <Form className="app-form">
                  <Input
                    type="select"
                    className="custom-form-select"
                    value={selectedFilter}
                    onChange={handleFilterChange}
                  >
                    <option value="Filter">Filter</option>
                    <option value="1">Fashion</option>
                    <option value="2">Books</option>
                    <option value="3">Sports</option>
                    <option value="4">Fitness</option>
                  </Input>
                </Form>
              </div>

              <ul className="product-category-list mt-3">
                {productCategories.map((category) => (
                  <li key={category.id} className={category.bgClass}>
                    <div>
                      <h6 className={`${category.textClass} mb-0`}>
                        {category.name}
                      </h6>
                    </div>
                    <div className="text-dark f-w-600 ms-2 flex-shrink-0">
                      {category.sales}
                      <span
                        className={`badge bg-white-300 ${category.textClass} f-w-700`}
                      >
                        {category.count}
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            </CardBody>
          </Card>
        </Col>

        <Col md="6" xxl="5">
          <Card>
            <CardBody className="p-0">
              {ApexCharts ? (
                <ApexCharts
                  options={EcommerceOrderData}
                  series={EcommerceOrderData.series}
                  type="bar"
                  height={324}
                />
              ) : (
                <Loading />
              )}
            </CardBody>
          </Card>
        </Col>

        <Effortless show />
        <ProjectCard />
        <ProjectFileData />
      </Row>
    </Container>
  );
};

export default WidgetsPage;
