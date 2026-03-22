"use client";
import { useState } from "react";
import { Badge, Container, Row, Col } from "react-bootstrap";
import { IconStack } from "@tabler/icons-react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { productlist } from "@/Data/Apps/Productlist/Productlist";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

const ProductList = () => {
  const [productPageList, setProductPageList] = useState(productlist);

  const handleDelete = (productId: number) => {
    const updatedProductList = productPageList.filter(
      (product) => product.id !== productId
    );
    setProductPageList(updatedProductList);
  };

  const handleEdit = (item: any) => {
    console.log("Edit item:", item);
  };

  const categoryColorMap: Record<string, string> = {
    Purse: "info",
    Watch: "success",
    Bag: "warning",
    Clothing: "danger",
  };

  const columns = [
    {
      key: "checkbox",
      header: <input type="checkbox" id="select-all" />,
      render: () => <input type="checkbox" />,
      className: "text-center",
    },
    {
      key: "product",
      header: "Product",
      render: (_: any, item: any) => (
        <div className="d-flex align-items-center">
          <div className="h-35 w-35 d-flex-center b-r-10 overflow-hidden me-2">
            <img src={item.image} alt={item.name} className="img-fluid" />
          </div>
          <span className="fw-medium">{item.name}</span>
        </div>
      ),
    },
    { key: "price", header: "Price" },
    { key: "stock", header: "Stock" },
    {
      key: "category",
      header: "Category",
      render: (_: any, item: any) => (
        <Badge bg={categoryColorMap[item.category] || "secondary"}>
          {item.category}
        </Badge>
      ),
    },
    { key: "seller", header: "Seller" },
    { key: "published", header: "Published" },
  ];

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Product List"
          title="Apps"
          path={["E-shop", "Product List"]}
          Icon={IconStack}
        />
        <Row>
          <Col xs={12}>
            <CustomDataTable
              data={productPageList}
              columns={columns}
              showActions={true}
              onEdit={handleEdit}
              onDelete={(item) => handleDelete(item.id)}
              title=""
              description=""
              pageLength={10}
            />
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ProductList;
