"use client";
import React, { useState } from "react";
import { Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { whishListData } from "@/Data/Apps/project details/projectteam";
import {
  IconHeart,
  IconHeartFilled,
  IconStack2,
  IconStarFilled,
} from "@tabler/icons-react";

const WishlistPage = () => {
  const handleIconClick = (index: number) => {
    setHeartStatus((prevState) => {
      const newStatus = [...prevState];
      newStatus[index] = !newStatus[index];
      return newStatus;
    });
  };
  const [heartStatus, setHeartStatus] = useState(
    new Array(whishListData.length).fill(false)
  );
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Wishlist"
          title="Apps"
          path={["E-shop", "Wishlist"]}
          Icon={IconStack2}
        />
        <Row className="wishlist-container">
          {whishListData.map((product, index) => (
            <div key={index} className="col-sm-6 col-lg-4 col-xl-3">
              <div className="card overflow-hidden">
                <div className="card-body p-0">
                  <div className="product-content-box">
                    <div className="product-grid">
                      <div className="product-image">
                        <a
                          href={product.productLink}
                          target="_blank"
                          className="image"
                        >
                          <img
                            className="pic-1"
                            src={product.image1}
                            alt={product.name}
                          />
                          <img
                            className="images_box"
                            src={product.image2}
                            alt={product.name}
                          />
                        </a>
                      </div>
                    </div>
                    <div className="p-3">
                      <div className="d-flex justify-content-between align-items-center">
                        <a
                          href={product.productLink}
                          className="m-0 f-s-20 f-w-500"
                        >
                          {product.name}
                        </a>
                        <p className="text-warning">
                          {product.rating}{" "}
                          <span className="text-warning">
                            <IconStarFilled size={18} />
                            <IconStarFilled size={18} />
                          </span>
                        </p>
                      </div>
                      <p className="text-secondary">{product.description}</p>
                      <div className="pricing-box">
                        <h6>
                          {product.price}{" "}
                          <span>
                            (<del>{product.originalPrice}</del>)
                          </span>
                          <span className="text-success ms-2">
                            {product.discount}
                          </span>
                        </h6>
                      </div>
                    </div>

                    <span
                      className={`bg-light-danger h-45 w-45 d-flex-center b-r-50 wishlist-like-icon`}
                      onClick={() => handleIconClick(index)} // Handle the click for each heart icon
                      role="button"
                      aria-label="Toggle Heart Icon"
                    >
                      {heartStatus[index] ? (
                        <IconHeartFilled
                          size={18}
                          className="heart-icon text-danger-dark"
                        />
                      ) : (
                        <IconHeart
                          size={18}
                          className="heart-icon text-danger-dark"
                        />
                      )}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </Row>
      </Container>
    </div>
  );
};

export default WishlistPage;
