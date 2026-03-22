import React, { useEffect, useState } from "react";
import {
  EcommerceOverviewReport,
  EcommerceSaleReport,
} from "@/Data/Charts/ApexCharts/ApexChart";
import {
  Col,
  Card,
  CardBody,
  Dropdown,
  DropdownMenu,
  DropdownItem,
  Form,
} from "reactstrap";
import { AlignLeft, Calendar, CreditCards, DollarCircle } from "iconoir-react";
import { IconChevronDown } from "@tabler/icons-react";

const EcommerceSale = () => {
  const [Chart, setChart] = useState<any>(null);
  const [overviewDropdownOpen, setOverviewDropdownOpen] = useState(false);

  useEffect(() => {
    const loadModules = async () => {
      const [chartModule] = await Promise.all([import("react-apexcharts")]);

      setChart(() => chartModule.default || chartModule);
    };

    if (typeof window !== "undefined") {
      loadModules();
    }
  }, []);

  const productCategories = [
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
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const toggleDropdown = () => setDropdownOpen(!dropdownOpen);
  const toggleOverviewDropdown = () =>
    setOverviewDropdownOpen(!overviewDropdownOpen);
  return (
    <>
      <Col md={6} xxl={3}>
        <div className="p-3">
          <h5>Sale Report</h5>
        </div>

        <Card>
          <CardBody>
            <div>
              {Chart ? (
                <Chart
                  options={EcommerceSaleReport}
                  series={EcommerceSaleReport.series}
                  type="donut"
                  height={285}
                />
              ) : (
                <div>Loading Chart...</div>
              )}
            </div>
          </CardBody>
        </Card>
      </Col>
      <Col md={6} xxl={3}>
        <div className="p-3">
          <h5>Product Category</h5>
        </div>
        <Card className="product-category-card">
          <CardBody>
            <div className="d-flex justify-content-between align-items-center">
              <Dropdown isOpen={dropdownOpen} toggle={toggleDropdown}>
                <button
                  className="btn btn-link text-dark text-decoration-none p-0 border-0 text-dark"
                  role="button"
                >
                  <AlignLeft
                    width={20}
                    height={20}
                    className="f-s-20 f-w-600 text-dark-dark"
                  />
                  <IconChevronDown
                    size={18}
                    className="ms-1 f-s-18 align-top"
                  />
                </button>
                <DropdownMenu end>
                  <DropdownItem>Last Month</DropdownItem>
                  <DropdownItem>Last Week</DropdownItem>
                  <DropdownItem>Last Year</DropdownItem>
                </DropdownMenu>
              </Dropdown>

              <Form className="app-form">
                <select
                  aria-label="Default select example"
                  className="form-select custom-form-select"
                  defaultValue="1"
                >
                  <option>Filter</option>
                  <option value="1">Fashion</option>
                  <option value="2">Books</option>
                  <option value="3">Sports</option>
                  <option value="4">Fitness</option>
                </select>
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
      <Col md={6} xxl={3}>
        <div className="p-3">
          <h5>Overview</h5>
        </div>
        <Card>
          <CardBody>
            <div>
              {Chart ? (
                <Chart
                  options={EcommerceOverviewReport}
                  series={EcommerceOverviewReport.series}
                  type="bar"
                  height={240}
                />
              ) : (
                <div>Loading Chart...</div>
              )}
            </div>
            <div className="d-flex justify-content-between align-items-center">
              <Dropdown
                isOpen={overviewDropdownOpen}
                toggle={toggleOverviewDropdown}
              >
                <button
                  aria-expanded="false"
                  className="btn btn-link text-dark text-decoration-none p-0 border-0"
                  role="button"
                >
                  <AlignLeft
                    height={20}
                    width={20}
                    className="f-s-20 f-w-600 text-dark-dark"
                  />
                  <IconChevronDown
                    size={20}
                    className="ms-1 f-s-18 align-top"
                  />
                </button>
                <DropdownMenu end>
                  <DropdownItem>Last Month</DropdownItem>
                  <DropdownItem>Last Week</DropdownItem>
                  <DropdownItem>Last Year</DropdownItem>
                </DropdownMenu>
              </Dropdown>

              <Form className="app-form">
                <select
                  aria-label="Default select example"
                  className="form-select custom-form-select"
                  defaultValue="1"
                >
                  <option>Jan</option>
                  <option value="1">Feb</option>
                  <option value="2">Mar</option>
                  <option value="3">..</option>
                  <option value="4">Dec</option>
                </select>
              </Form>
            </div>
          </CardBody>
        </Card>
      </Col>
      <Col md={6} xxl={3}>
        <div className="p-3">
          <h5>Transaction</h5>
        </div>
        <Card className="transaction-card">
          <CardBody>
            <div className="text-center">
              <img alt="logo-img" src="/images/form/done.png" />
              <h6 className="text-success-dark mb-0">Thank You!</h6>
              <p className="mb-0 f-w-600 text-success d-inline transaction-txt">
                Your transaction was successful
              </p>
              <img
                alt="gif"
                className="w-30 d-inline align-text-bottom"
                src="/images/dashboard/ecommerce-dashboard/celebration.gif"
              />
            </div>

            <div className="custom-divider"></div>

            <div className="d-flex justify-content-between">
              <div>
                <p className="text-dark f-w-500 mb-0">
                  <CreditCards
                    height={20}
                    width={20}
                    className="f-s-16 align-text-top me-2"
                  />
                  Transaction ID
                </p>
                <h6 className="text-success-dark">568368657681</h6>
              </div>
              <div>
                <p className="text-dark f-w-500 mb-0">
                  <DollarCircle
                    height={20}
                    width={20}
                    className="f-s-16 align-text-top me-2"
                  />
                  Amount
                </p>
                <h6 className="text-success-dark">$68.00</h6>
              </div>
            </div>

            <div className="mt-3">
              <p className="text-dark f-w-500 mb-0">
                <Calendar
                  height={20}
                  width={20}
                  className="f-s-16 align-text-top me-2"
                />
                Date & Time
              </p>
              <h6 className="mb-0 text-success-dark">15 Jun 2024 • 6:90PM</h6>
            </div>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default EcommerceSale;
